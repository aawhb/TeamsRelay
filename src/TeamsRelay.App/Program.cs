using TeamsRelay.App;
using TeamsRelay.Core;

using var cancellationTokenSource = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellationTokenSource.Cancel();
};

return await new CliApplication(Console.In, Console.Out, Console.Error, AppEnvironment.Detect()).RunAsync(args, cancellationTokenSource.Token);
