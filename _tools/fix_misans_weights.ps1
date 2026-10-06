# Merge MiSans-Medium / MiSans-Demibold into the "MiSans" family and give them
# standard weights (500 / 600). Official MiSans ttf uses non-standard usWeightClass
# values (Regular 330 / Medium 380 / Demibold 450 / Bold 630) and Medium/Demibold
# live in their own families, so Avalonia can never match weight 500/600 inside "MiSans".
#
# How: rebuild the 'name' table (platform 3 records: family/subfamily/full/postscript/
# typographic family), append it at the end of the file, update the table directory
# entry + checksum, then recompute head.checkSumAdjustment.

$ErrorActionPreference = 'Stop'
$dir = "E:\ClassNex\src\ClassNex\Assets\Fonts"

function U16BE([byte[]]$b, [int]$o) { return [int]($b[$o] * 256 + $b[$o + 1]) }
function U32BE([byte[]]$b, [int]$o) { return [uint32]($b[$o] * 16777216 + $b[$o + 1] * 65536 + $b[$o + 2] * 256 + $b[$o + 3]) }
function BE16([int]$v) { return [byte[]]@([byte](($v -shr 8) -band 0xFF), [byte]($v -band 0xFF)) }
function BE32([uint32]$v) { return [byte[]]@([byte](($v -shr 24) -band 0xFF), [byte](($v -shr 16) -band 0xFF), [byte](($v -shr 8) -band 0xFF), [byte]($v -band 0xFF)) }

function Get-Tables([byte[]]$b) {
  $nt = U16BE $b 4
  $list = @()
  for ($i = 0; $i -lt $nt; $i++) {
    $o = 12 + $i * 16
    $tag = [System.Text.Encoding]::ASCII.GetString($b, $o, 4)
    $list += [pscustomobject]@{
      Tag = $tag; DirOffset = $o
      Offset = [int](U32BE $b ($o + 8))
      Length = [int](U32BE $b ($o + 12))
    }
  }
  return $list
}

function Map-Name([int]$nid, [string]$subName) {
  switch ($nid) {
    1 { return "MiSans" }
    2 { return $subName }
    3 { return "MiSans $subName; ClassNex build" }
    4 { return "MiSans $subName" }
    6 { return "MiSans-$subName" }
    16 { return "MiSans" }
    17 { return $subName }
    default { return $null }
  }
}

function Build-NameTable([byte[]]$b, [int]$nameOff, [string]$subName) {
  $count = U16BE $b ($nameOff + 2)
  $strOff = U16BE $b ($nameOff + 4)
  $recs = @()
  for ($i = 0; $i -lt $count; $i++) {
    $r = $nameOff + 6 + $i * 12
    $plat = U16BE $b $r
    $enc = U16BE $b ($r + 2)
    $lang = U16BE $b ($r + 4)
    $nid = U16BE $b ($r + 6)
    $len = U16BE $b ($r + 8)
    $off = U16BE $b ($r + 10)
    $bytes = New-Object byte[] $len
    [Array]::Copy($b, $nameOff + $strOff + $off, $bytes, 0, $len)
    if ($plat -eq 3) {
      $new = Map-Name $nid $subName
      if ($null -ne $new) {
        $bytes = [System.Text.Encoding]::BigEndianUnicode.GetBytes($new)
      }
    }
    $recs += [pscustomobject]@{ Plat = $plat; Enc = $enc; Lang = $lang; Nid = $nid; Bytes = $bytes }
  }

  $out = New-Object System.Collections.Generic.List[byte]
  foreach ($x in (BE16 0)) { $out.Add($x) }
  foreach ($x in (BE16 $recs.Count)) { $out.Add($x) }
  foreach ($x in (BE16 (6 + 12 * $recs.Count))) { $out.Add($x) }
  $strings = New-Object System.Collections.Generic.List[byte]
  foreach ($rec in $recs) {
    foreach ($x in (BE16 $rec.Plat)) { $out.Add($x) }
    foreach ($x in (BE16 $rec.Enc)) { $out.Add($x) }
    foreach ($x in (BE16 $rec.Lang)) { $out.Add($x) }
    foreach ($x in (BE16 $rec.Nid)) { $out.Add($x) }
    foreach ($x in (BE16 $rec.Bytes.Length)) { $out.Add($x) }
    foreach ($x in (BE16 $strings.Count)) { $out.Add($x) }
    foreach ($x in $rec.Bytes) { $strings.Add($x) }
  }
  foreach ($x in $strings) { $out.Add($x) }
  while ($out.Count % 4 -ne 0) { $out.Add(0) }
  return $out.ToArray()
}

function TableChecksum([byte[]]$b, [int]$off, [int]$len) {
  $sum = [uint64]0
  for ($i = 0; $i -lt $len; $i += 4) {
    $v = [uint32]0
    if ($i + 0 -lt $len) { $v = $v -bor ([uint32]$b[$off + $i] -shl 24) }
    if ($i + 1 -lt $len) { $v = $v -bor ([uint32]$b[$off + $i + 1] -shl 16) }
    if ($i + 2 -lt $len) { $v = $v -bor ([uint32]$b[$off + $i + 2] -shl 8) }
    if ($i + 3 -lt $len) { $v = $v -bor ([uint32]$b[$off + $i + 3]) }
    $sum += [uint64]$v
  }
  return [uint32]($sum -band [uint64]4294967295)
}

function Fix-Font([string]$path, [int]$newWeight, [string]$subName) {
  $b = [System.IO.File]::ReadAllBytes($path)
  $tables = Get-Tables $b
  $nameEntry = $tables | Where-Object { $_.Tag -eq 'name' } | Select-Object -First 1
  $headEntry = $tables | Where-Object { $_.Tag -eq 'head' } | Select-Object -First 1
  $os2Entry = $tables | Where-Object { $_.Tag -eq 'OS/2' } | Select-Object -First 1

  # 1) OS/2 usWeightClass
  $b[$os2Entry.Offset + 4] = [byte](($newWeight -shr 8) -band 0xFF)
  $b[$os2Entry.Offset + 5] = [byte]($newWeight -band 0xFF)

  # 2) rebuild name table
  $newName = Build-NameTable $b $nameEntry.Offset $subName

  # 3) append (4-byte aligned)
  $appendAt = $b.Length
  while ($appendAt % 4 -ne 0) { $appendAt++ }
  $final = New-Object byte[] ($appendAt + $newName.Length)
  [Array]::Copy($b, 0, $final, 0, $b.Length)
  [Array]::Copy($newName, 0, $final, $appendAt, $newName.Length)

  # 4) update the 'name' directory entry (checksum / offset / length)
  $sum = TableChecksum $final $appendAt $newName.Length
  $final[$nameEntry.DirOffset + 4] = (BE32 $sum)[0]
  $final[$nameEntry.DirOffset + 5] = (BE32 $sum)[1]
  $final[$nameEntry.DirOffset + 6] = (BE32 $sum)[2]
  $final[$nameEntry.DirOffset + 7] = (BE32 $sum)[3]
  $offBytes = BE32 ([uint32]$appendAt)
  for ($i = 0; $i -lt 4; $i++) { $final[$nameEntry.DirOffset + 8 + $i] = $offBytes[$i] }
  $lenBytes = BE32 ([uint32]$newName.Length)
  for ($i = 0; $i -lt 4; $i++) { $final[$nameEntry.DirOffset + 12 + $i] = $lenBytes[$i] }

  # 5) recompute head.checkSumAdjustment
  $ho = $headEntry.Offset
  for ($i = 8; $i -le 11; $i++) { $final[$ho + $i] = 0 }
  $sum2 = [uint64]0
  for ($i = 0; $i -lt $final.Length; $i += 4) {
    $v = [uint32]0
    if ($i + 0 -lt $final.Length) { $v = $v -bor ([uint32]$final[$i] -shl 24) }
    if ($i + 1 -lt $final.Length) { $v = $v -bor ([uint32]$final[$i + 1] -shl 16) }
    if ($i + 2 -lt $final.Length) { $v = $v -bor ([uint32]$final[$i + 2] -shl 8) }
    if ($i + 3 -lt $final.Length) { $v = $v -bor ([uint32]$final[$i + 3]) }
    $sum2 += [uint64]$v
  }
  $target = [uint64]2981146554  # 0xB1B0AFBA
  $diff = $target - ([uint64]([uint32]($sum2 -band [uint64]4294967295)))
  if ($diff -lt 0) { $diff += 4294967296 }
  $adjustBytes = BE32 ([uint32]$diff)
  for ($i = 0; $i -lt 4; $i++) { $final[$ho + 8 + $i] = $adjustBytes[$i] }

  [System.IO.File]::WriteAllBytes($path, $final)
  $leaf = Split-Path $path -Leaf
  "patched $leaf : usWeightClass=$newWeight family=MiSans subfamily=$subName size=$($final.Length)"
}

Fix-Font "$dir\MiSans-Medium.ttf" 500 "Medium"
Fix-Font "$dir\MiSans-Demibold.ttf" 600 "Demibold"

# verify
foreach ($w in @("Regular", "Medium", "Demibold", "Bold")) {
  $b = [System.IO.File]::ReadAllBytes("$dir\MiSans-$w.ttf")
  $tables = Get-Tables $b
  $nameEntry = $tables | Where-Object { $_.Tag -eq 'name' } | Select-Object -First 1
  $os2Entry = $tables | Where-Object { $_.Tag -eq 'OS/2' } | Select-Object -First 1
  $no = $nameEntry.Offset
  $cnt = U16BE $b ($no + 2); $so = U16BE $b ($no + 4)
  $fam = ""; $sub = ""
  for ($i = 0; $i -lt $cnt; $i++) {
    $r = $no + 6 + $i * 12
    $plat = U16BE $b $r; $nid = U16BE $b ($r + 6); $len = U16BE $b ($r + 8); $off = U16BE $b ($r + 10)
    if ($plat -eq 3 -and $nid -eq 1 -and $fam -eq "") { $fam = [System.Text.Encoding]::BigEndianUnicode.GetString($b, $no + $so + $off, $len) }
    if ($plat -eq 3 -and $nid -eq 2 -and $sub -eq "") { $sub = [System.Text.Encoding]::BigEndianUnicode.GetString($b, $no + $so + $off, $len) }
  }
  $wt = U16BE $b ($os2Entry.Offset + 4)
  "check MiSans-$w : usWeightClass=$wt family='$fam' subfamily='$sub'"
}
