using MMOGameServer.Messages.Contracts;
using MMOGameServer.Messages.Requests;
using MMOGameServer.Models;

namespace MMOGameServer.Handlers.Communication;

public class PingHandler : IMessageHandler<PingMessage>
{
    public async Task HandleAsync(ConnectedClient client, PingMessage message)
    {
        // Send pong response with the same timestamp
        await client.SendMessageAsync(new
        {
            type = "pong",
            timestamp = message.Timestamp
        });
    }
}