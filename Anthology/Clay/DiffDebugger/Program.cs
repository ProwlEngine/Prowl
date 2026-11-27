using Prowl.Clay.DiffDebugger;

if (args.Length != 2)
{
    Console.WriteLine("Prowl.Clay.DiffDebugger");
    Console.WriteLine();
    Console.WriteLine("Loads two model files through Prowl.Clay and prints a structural diff:");
    Console.WriteLine("header counts, hierarchy, skin / inverse-bind matrices, animation channels.");
    Console.WriteLine("Names are normalized (strip common prefixes and trailing -NN indices) so the");
    Console.WriteLine("same model exported through different pipelines compares cleanly.");
    Console.WriteLine();
    Console.WriteLine("Usage: Prowl.Clay.DiffDebugger <model-a> <model-b>");
    return 1;
}

return ModelDiff.Run(args[0], args[1]);
