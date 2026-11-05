using System.Collections.Concurrent;
using System.Text;
using WinTimeline.Inference.Host;
using WinTimeline.Inference.Native;

// WT166: reads WinTimeline.Inference's protocol on stdin, one JSON line a message, and answers on
// stdout. One worker thread owns the engine and runs in order, so a cancel line is read while a
// run goes on. Closing stdin ends the host once the current run is done.
//
//   WinTimeline.Inference.Host --fake
//   WinTimeline.Inference.Host --engine llama.cpp --engines <folder>

Console.InputEncoding = new UTF8Encoding(false);
Console.OutputEncoding = new UTF8Encoding(false);
Lock writing = new();
void Say(HostMessage message)
{
    lock (writing)
    {
        Console.Out.WriteLine(message.ToLine());
        Console.Out.Flush();
    }
}

IEngine engine;
try
{
    engine = Engines.Create(args);
}
catch (ArgumentException failure)
{
    // The client reads this from stderr when the host stops.
    Console.Error.WriteLine(failure.Message);
    return 2;
}

using BlockingCollection<HostMessage> work = [];
Thread worker = new(() =>
{
    using (engine)
    {
        foreach (HostMessage message in work.GetConsumingEnumerable())
        {
            try
            {
                if (message.Type == "load")
                {
                    engine.Load(message.Model!);
                    Say(new HostMessage("loaded"));
                    continue;
                }

                StringBuilder output = new();
                bool finished = engine.Run(message.Prompt!, piece =>
                {
                    output.Append(piece);
                    Say(new HostMessage("token", Text: piece));
                });
                Say(finished ? new HostMessage("done", Text: output.ToString()) : new HostMessage("failed", Message: "Cancelled.", Cancelled: true));
            }
            catch (Exception failure) when (failure is IOException or ArgumentException or InvalidOperationException or DllNotFoundException)
            {
                Say(new HostMessage("failed", Message: failure.Message));
            }
        }
    }
})
{
    Name = "Engine",
};
worker.Start();

while (Console.In.ReadLine() is { } line)
{
    switch (HostMessage.Parse(line))
    {
        case { Type: "cancel" }:
            engine.Cancel();
            break;
        case { Type: "load", Model: not null } load:
            work.Add(load);
            break;
        case { Type: "run", Prompt: not null } run:
            engine.Reset();
            work.Add(run);
            break;
    }
}

work.CompleteAdding();
worker.Join();
return 0;
