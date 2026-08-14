using TornadoEos.Hardware;

Console.OutputEncoding = System.Text.Encoding.UTF8;
Console.WriteLine("== Tornado EOS · PTP/WPD property probe (read-only) ==");
Console.WriteLine();

var result = CameraDiagnostics.Run(msg => Console.WriteLine($"  {msg}"));
Console.WriteLine();
Console.Write(result.ReportText);
