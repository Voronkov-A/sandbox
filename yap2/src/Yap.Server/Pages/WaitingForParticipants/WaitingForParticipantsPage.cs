using System;
using System.Linq;
using Yap.Common;
using Yap.Common.Commands;
using Yap.Common.Events;
using Yap.Server.Model;
using Yap.Server.Pages.Partying;

namespace Yap.Server.Pages.WaitingForParticipants;

public sealed class WaitingForParticipantsPage
{
    private readonly ICommandReader _commandReader;
    private readonly IEventWriter _eventWriter;
    private readonly World _world;

    public Type? Handle()
    {
        while (_commandReader.TryRead(out var envelope))
        {
            switch (envelope.Command)
            {
                case BindParticipantToIdentityCommand bindParticipantToIdentityCommand:
                    {
                        var identity = new Identity(bindParticipantToIdentityCommand.Identity);

                        if (identity != envelope.Originator)
                        {
                            _eventWriter.Write(new IdentityBlockedEvent
                            {
                                Identity = envelope.Originator.ToString(),
                                Reason = "Protocol violation."
                            });
                            break;
                        }

                        if (_world.Participants.Any(x => x.Identity == identity))
                        {
                            break;
                        }

                        var participant = _world.Participants
                            .FirstOrDefault(x => x.Id == bindParticipantToIdentityCommand.ParticipantId);

                        if (participant != null && participant.Identity == null)
                        {
                            participant.Identity = identity;

                            _eventWriter.Write(new ParticipantBoundToIdentityEvent
                            {
                                Identity = identity.ToString(),
                                ParticipantId = participant.Id
                            });
                        }

                        if (_world.Participants.All(x => x.Identity != null))
                        {
                            _eventWriter.Write(new PartyStartedEvent());
                            return typeof(PartyingPage);
                        }

                        break;
                    }
                default:
                    {
                        throw new InvalidOperationException($"{envelope.GetType()} is not supported.");
                    }
            }
        }
    }
}
