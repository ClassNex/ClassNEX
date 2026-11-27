#requires -Version 7.2
param([string]$CiSource = 'E:/ci-source')
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$pin = '235914a2fac1733cc2a169457c7bad4edb2ca2f6'
function Original([string]$file) {
    $text = git -C $CiSource show "${pin}:$file"
    if ($LASTEXITCODE) { throw "Missing upstream file: $file" }
    ($text -join "`n").TrimStart([char]0xFEFF)
}
$checked = 0
function Same($a, $b, [string]$label) {
    if ($null -eq $a -or "$a" -cne "$b") { throw "${label}: reference '$a', candidate '$b'" }; $script:checked++
}
foreach ($file in 'SlidingListBox.axaml','Layers.axaml') {
    $up = git -C $CiSource rev-parse "${pin}:ClassIsland.Core/Themes/$file"
    $ours = git hash-object --no-filters (Join-Path $repo "src/ClassNex/Styles/Upstream/ClassIsland/$file")
    Same $up $ours $file
}
foreach ($file in 'lucide.ttf','FluentSystemIcons-Resizable.ttf') {
    $up = git -C $CiSource rev-parse "${pin}:ClassIsland.Core/Assets/Fonts/$file"
    $ours = git hash-object --no-filters (Join-Path $repo "src/ClassNex/Assets/NotificationFonts/$file")
    Same $up $ours $file
}
foreach ($face in 'Light','Regular','Medium','Bold') {
    $file = "HarmonyOS_Sans_SC_$face.ttf"
    Same (git -C $CiSource rev-parse "${pin}:ClassIsland/Assets/Fonts/$file") (git hash-object --no-filters (Join-Path $repo "src/ClassNex/Assets/Fonts/$file")) $file
}
function ReadXaml([string]$text) {
    $document = [xml.xmlDocument]::new()
    $document.PreserveWhitespace = $true
    $document.LoadXml($text)
    $document
}
$ref = ReadXaml (Original 'ClassIsland/Controls/NotificationProviders/ClassNotificationProviderControl.axaml')
$nex = ReadXaml (Get-Content -Raw -LiteralPath (Join-Path $repo 'src/ClassNex/Views/MainWindow.axaml'))
function Named([xml]$doc, [string]$name) { $doc.SelectSingleNode("//*[@*[local-name()='Name']='$name']") }
function Attributes($reference,$candidate,[string[]]$names) {
    if (!$reference -or !$candidate) { throw 'A provider element was not found' }
    foreach ($att in $names) { if (!$reference.HasAttribute($att)) { throw "Missing reference attribute: $att" }; Same $reference.GetAttribute($att) $candidate.GetAttribute($att) "$($candidate.LocalName).$att" }
}
function RequireAttribute($node,[string]$name,[string]$expected) {
    if (!$node -or !$node.HasAttribute($name) -or $node.GetAttribute($name) -cne $expected) { throw "Unexpected provider layout: expected $name='$expected'" }
}
function InlineLayout($caption) {
    $parts = @(); $separator = ''
    foreach ($child in $caption.ChildNodes) {
        if ($child.NodeType -eq 'Element' -and $child.LocalName -eq 'Run') {
            $text = $child.GetAttribute('Text'); if (!$text -or $text.StartsWith('{Binding')) { $text = '<value>' }
            $parts += "$separator$text|$($child.GetAttribute('FontWeight'))|$($child.GetAttribute('Classes'))"; $separator = ''
        } elseif ($parts.Count -and $child.NodeType -in 'Whitespace','Text','SignificantWhitespace' -and $child.Value) { $separator = $child.Value -replace '\s+',' ' }
    }
    if (!$parts.Count) { throw 'Expected a caption composed of runs' }
    $parts -join ';'
}
$coursePage = Named $nex 'NotificationOverlayCoursePage'
RequireAttribute $coursePage 'ColumnDefinitions' 'Auto,Auto'
RequireAttribute $coursePage 'HorizontalAlignment' 'Center'
RequireAttribute $coursePage 'VerticalAlignment' 'Center'
RequireAttribute (Named $nex 'NotificationOverlayTime') 'Margin' '2 0 0 0'
foreach ($provider in 'ClassPrepareNotifyOverlay','ClassOffOverlay') {
    $grid = $ref.SelectSingleNode("//*[@*[local-name()='Key']='$provider']")
    RequireAttribute $grid 'ColumnDefinitions' '*,*'
    $caption = $grid.SelectSingleNode("./*[local-name()='TextBlock']")
    $candidateCaption = Named $nex $(if ($provider -eq 'ClassOffOverlay') { 'NotificationBreakCaption' } else { 'NotificationOverlayLeft' })
    Attributes $caption $candidateCaption @('Grid.Column','Margin','HorizontalAlignment','VerticalAlignment')
    Same (InlineLayout $caption) (InlineLayout $candidateCaption) "$provider inline order, spacing, weight and hierarchy"
    $lesson = $grid.SelectSingleNode("./*[local-name()='StackPanel']")
    Attributes $lesson (Named $nex 'NotificationLesson') @('Grid.Column','Margin','HorizontalAlignment','VerticalAlignment','Orientation')
    $labels = $lesson.SelectNodes("./*[local-name()='TextBlock']")
    Attributes $labels[0] (Named $nex 'NotificationOverlayPrefix') @('Classes','VerticalAlignment')
    Attributes $labels[1] (Named $nex 'NotificationOverlaySubject') @('FontSize','FontWeight','VerticalAlignment')
    Attributes $labels[2] (Named $nex 'NotificationOverlayTeacher') @('FontSize','Margin','VerticalAlignment','Classes')
    RequireAttribute $labels[3] 'Margin' '2 0 0 0'
    Attributes $labels[3] (Named $nex 'NotificationOverlayTime') @('FontSize','VerticalAlignment','Classes')
}
[xml]$dismiss = Original 'ClassIsland/Controls/NotificationProviders/AfterSchoolNotificationProvider.axaml'
Attributes ($dismiss.SelectSingleNode("//*[local-name()='TextBlock']")) (Named $nex 'NotificationSimpleMessage') @('Margin','VerticalAlignment','HorizontalAlignment','TextTrimming')
[xml]$mask = Original 'ClassIsland.Core/Controls/NotificationTemplates/TwoIconsMaskTemplate.axaml'
Attributes ($mask.SelectSingleNode("//*[local-name()='TextBlock']")) (Named $nex 'NotificationMaskText') @('Margin','FontWeight','FontSize')
foreach ($icon in 'NotificationMaskLeftIcon','NotificationMaskRightIcon') {
    $slot = (Named $nex $icon).ParentNode
    Attributes ($mask.SelectSingleNode("//*[local-name()='IconSourceElement']")) $slot @('Width','Height','VerticalAlignment')
}
$theme = ReadXaml (Original 'ClassIsland/XamlThemes/FluentTheme/Styles.axaml')
$ours = ReadXaml (Get-Content -Raw -LiteralPath (Join-Path $repo 'src/ClassNex/Styles/ClassIslandClassroomNotification.axaml'))
Attributes (Named $theme 'SlantedMaskControlSeperated') (Named $nex 'NotificationMaskBg') @('ClipToBounds','RegionDurationMs','StageStaggerMs','Fill')
Attributes ($theme.SelectSingleNode("//*[local-name()='DoubleTransition' and @Property='BackgroundWidth']")) ($ours.SelectSingleNode("//*[local-name()='DoubleTransition' and @Property='Width']")) @('Duration','Easing')
function MotionSignature($node) {
    if (!$node) { throw 'Missing animation node' }
    $attributes = @($node.Attributes | ForEach-Object { "$($_.LocalName)=$($_.Value)" })
    if ($node.LocalName -eq 'Animation' -and !$node.HasAttribute('IterationCount')) { $attributes += 'IterationCount=1' }
    $children = @($node.SelectNodes('./*') | ForEach-Object { MotionSignature $_ })
    "$($node.LocalName)[$(($attributes | Sort-Object) -join ';')]($($children -join ','))"
}
foreach ($state in 'in','out') {
    $flag = if ($state -eq 'in') { 'entering' } else { 'revealing' }
    foreach ($stages in @(@('OverlayMaskContent','NotificationMaskContent'),@('BackgroundBorderOverlayMask','NotificationMaskShadow'))) {
        $reference = $theme.SelectSingleNode("//*[local-name()='Style' and @Selector='^:mask-$state']/*[local-name()='Style' and contains(@Selector,'#$($stages[0])')]/*[local-name()='Style.Animations']")
        $target = if ($stages[1] -eq 'NotificationMaskShadow') { $nex } else { $ours }
        $candidate = $target.SelectSingleNode("//*[local-name()='Style' and @Selector='Border#$($stages[1]).$flag']/*[local-name()='Style.Animations']")
        Same (MotionSignature $reference) (MotionSignature $candidate) "$flag $($stages[0]) keyframes"
    }
}
$originalRipple = ReadXaml (Original 'ClassIsland/Controls/NotificationEffects/RippleEffect.axaml')
$ripple = ReadXaml (Get-Content -Raw -LiteralPath (Join-Path $repo 'src/ClassNex/Controls/NotificationEffects/RippleEffect.axaml'))
Attributes (Named $originalRipple 'EllipseMain') (Named $ripple 'EllipseMain') @('StrokeThickness','Canvas.Left','Canvas.Top','Fill','RenderTransformOrigin','VerticalAlignment','HorizontalAlignment')
Attributes ($originalRipple.SelectSingleNode("//*[local-name()='TranslateTransform']")) ($ripple.SelectSingleNode("//*[local-name()='TranslateTransform']")) @('X','Y')
function MotionStatements([string]$originalFile,[string]$candidateFile,[string]$pattern,[string]$label) {
    function Statements([string]$text) {
        $code = (($text -replace '(?m)//[^\r\n]*','') -replace '\s+','')
        [regex]::Matches($code,$pattern) | ForEach-Object Value
    }
    $a = @(Statements (Original $originalFile)); $b = @(Statements (Get-Content -Raw -LiteralPath (Join-Path $repo $candidateFile)))
    if (!$a.Count) { throw "Unrecognized upstream motion: $label" }
    Same $a.Count $b.Count $label
    for ($i=0; $i -lt $a.Count; $i++) { Same $a[$i] $b[$i] "$label $i" }
}
MotionStatements 'ClassIsland/Controls/NotificationEffects/RippleEffect.axaml.cs' 'src/ClassNex/Controls/NotificationEffects/RippleEffect.axaml.cs' 'animation(?:Scale|Opacity)\.(?:InsertKeyFrame\(|Duration=)[^;]+;|awaitTask\.Delay\([^;]+;|varr(?:11|12|21|22)=[^;]+;' 'full-screen ripple coordinates, keyframes and lifetime'
MotionStatements 'ClassIsland/Controls/SlantedMaskControl.cs' 'src/ClassNex/Controls/SlantedMaskControl.cs' 'animation\.Children\.AddRange\([^;]+;|varp[1-4]=newPoint\([^;]+;|doublefullW=[^;]+;' 'five masks, segment timing and edges'
"PASS: $checked direct comparisons against $pin (templates, fonts, shared geometry, text and motion)."
'Separate layout contract: upstream uses equal columns; ClassNEX centers a content-sized row. The time margin is 2 DIP in XAML; the rendered 2/6 DIP policy is checked separately by compare_ci_notification_provider.ps1 -LayoutContract Compact.'
