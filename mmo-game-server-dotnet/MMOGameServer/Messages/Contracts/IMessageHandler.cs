using MMOGameServer.Models;

namespace MMOGameServer.Messages.Contracts;

public interface IMessageHandler<TMessage> where TMessage : IGameMessage
{
    Task HandleAsync(ConnectedClient client, TMessage message);
}