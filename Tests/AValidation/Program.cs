using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using BlackWhale.Shared;
using BlackWhale.Network;
using BlackWhale.Tests;

internal sealed class TestJson : ITestJson, IWireCodec
{
    private readonly JsonSerializerOptions options = new JsonSerializerOptions { IncludeFields = true };
    public T Decode<T>(string json)
    {
        try { return JsonSerializer.Deserialize<T>(json, options); }
        catch (JsonException ex) { throw new FormatException("Invalid test JSON", ex); }
    }
    public string Encode<T>(T value) { return JsonSerializer.Serialize(value, options); }
    public string EncodeClient(ClientMessage message) { return Encode(message); }
    public ClientMessage DecodeClient(string json) { RequestJsonValidator.Validate(json); return Decode<ClientMessage>(json); }
    public string EncodeServer(ServerMessage message) { return Encode(message); }
    public ServerMessage DecodeServer(string json) { return Decode<ServerMessage>(json); }
}
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        try
        {
            string root = args.Length > 0 ? Path.GetFullPath(args[0]) : Directory.GetCurrentDirectory();
            var codec = new TestJson();
            var suite = new RoomValidation(root, codec);
            string[] core = suite.RunAll();
            string[] network = await TransportValidation.Run(suite, codec);
            string[] realC = await RealInformationValidation.Run(suite, codec);
            foreach (string check in core.Concat(network).Concat(realC)) Console.WriteLine("PASS " + check);
            string output = Path.Combine(root, "Logs", "AValidation-results.json");
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            File.WriteAllText(output, JsonSerializer.Serialize(new { runtime = ".NET 10", serviceMode = "A_FIXTURE_MOCKS_AND_REAL_C",
                core, network, realC, notVerified = new[] { "Real B service", "Four physical PCs", "Unity player UI", "Auctions and multi-turn progression" } },
                new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine("PASS " + (core.Length + network.Length + realC.Length) + " checks. Evidence: " + output);
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
