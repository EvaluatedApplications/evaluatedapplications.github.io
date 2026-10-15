using HoloDb;
using HoloDb.Protocol;

// A server loop built on the wire protocol (compile-checked here; it needs a connected stream to run)
async Task HandleOneRequest(Stream networkStream, HoloDbService engine, CancellationToken ct)
{
    var payload = await Wire.ReadFrameAsync(networkStream, ct);
    var request = HoloProtocol.DecodeRequest(payload);

    if (request.Op == WireOp.Exec)
    {
        var result = await engine.ExecuteAsync(request.Sql);
        var response = HoloProtocol.Result(result);
        await Wire.WriteFrameAsync(networkStream, response, ct);
    }
}
Console.WriteLine(nameof(HandleOneRequest));