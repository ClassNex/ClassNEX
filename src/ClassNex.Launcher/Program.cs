using System.Diagnostics;

var root = Path.GetDirectoryName(Environment.ProcessPath) ?? Directory.GetCurrentDirectory();

var payloadExe = Directory.GetDirectories(root, "app-*")
    .OrderByDescending(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
    .Select(dir => Path.Combine(dir, "ClassNex.exe"))
    .FirstOrDefault(File.Exists);

if (payloadExe is null)
{
    Console.WriteLine("未找到应用文件（app-*/ClassNex.exe）。请重新解压完整的发布包后重试。");
    return 1;
}

var startInfo = new ProcessStartInfo(payloadExe)
{
    UseShellExecute = true,
    WorkingDirectory = Path.GetDirectoryName(payloadExe)!,
};

foreach (var arg in args)
    startInfo.ArgumentList.Add(arg);

Process.Start(startInfo);
return 0;
