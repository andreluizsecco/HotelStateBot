using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Dialogs;
using System;

namespace HotelStateBot
{
    public class BotAccessors
    {
        public BotAccessors(ConversationState conversationState, UserState userState)
        {
            ConversationState = conversationState ?? throw new ArgumentNullException(nameof(conversationState));
            UserState = userState ?? throw new ArgumentNullException(nameof(userState));
        }

        public IStatePropertyAccessor<DialogState> DialogStateAccessor { get; set; }

        public IStatePropertyAccessor<UserProfile> UserProfileAccessor { get; set; }

        public ConversationState ConversationState { get; }

        public UserState UserState { get; }
    }
}
