using EnvSync.Cli.Commands;

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

var application = new CliApplication(Console.Out, Console.Error);
return await application.RunAsync(args, cts.Token);
