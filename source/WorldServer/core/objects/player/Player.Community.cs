using System;

namespace WorldServer.core.objects
{
    public partial class Player
    {
        // the client asked for the community goals (COMMUNITY_REQUEST), so it shows their popups
        public bool CommunitySubscribed;
        // the last answer and the last claim, to keep Redis from being asked too often
        public DateTime CommunityAsked, CommunityClaimed;
    }
}
