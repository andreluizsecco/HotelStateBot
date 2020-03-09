using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Builder.Dialogs.Choices;
using Microsoft.Bot.Schema;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace HotelStateBot.Bots
{
    public partial class HotelBot : IBot
    {
        private readonly DialogSet _dialogs;
        private readonly BotAccessors _botAccessors;

        public HotelBot(BotAccessors botAccessors)
        {
            _botAccessors = botAccessors;

            _dialogs = new DialogSet(botAccessors.DialogStateAccessor);
            _dialogs
                .Add(new TextPrompt(Constants.NamePrompt))
                .Add(new NumberPrompt<int>(Constants.AgePrompt))
                .Add(new ChoicePrompt(Constants.RoomTypeSelectionPrompt))
                .Add(new ChoicePrompt(Constants.PaymentTypeSelectionPrompt))
                .Add(new ChoicePrompt(Constants.ConfirmPrompt));

            // Add the dialogs we need to the dialog set.
            _dialogs.Add(new WaterfallDialog(Constants.RootDialog)
                .AddStep(NameStepAsync)
                .AddStep(AgeStepAsync)
                .AddStep(RoomTypeSelectionStepAsync)
                .AddStep(PaymentTypeSelectionStepAsync)
                .AddStep(FormCompletedStepAsync));

            _dialogs.Add(new WaterfallDialog(Constants.ReviewDialog)
                .AddStep(BookingConfirmationStepAsync)
                .AddStep(FinishStepAsync));

            _dialogs.Add(new WaterfallDialog(Constants.OnHoldDialog)
                .AddStep(OnHoldStepAsync)
                .AddStep(ContinueToHoldStepAsync));
        }

        public async Task OnTurnAsync(ITurnContext turnContext, CancellationToken cancellationToken = default)
        {
            if (turnContext.Activity.Type == ActivityTypes.Message)
            {
                var message = turnContext.Activity.Text?.Trim();
                var dialogContext = await _dialogs.CreateContextAsync(turnContext, cancellationToken);

                if (string.Equals(message, Constants.HelpCommand, StringComparison.InvariantCultureIgnoreCase))
                {
                    await turnContext.SendActivityAsync(HelpText, cancellationToken: cancellationToken);
                    return;
                }

                if (dialogContext.ActiveDialog?.Id != Constants.OnHoldDialog)
                {
                    if (string.Equals(message, Constants.WaitCommand, StringComparison.InvariantCultureIgnoreCase))
                    {
                        await dialogContext.BeginDialogAsync(Constants.OnHoldDialog, null, cancellationToken);
                        await _botAccessors.ConversationState.SaveChangesAsync(turnContext, false, cancellationToken);
                        return;
                    }
                }

                if (string.Equals(message, Constants.CancelCommand, StringComparison.InvariantCultureIgnoreCase))
                {
                    await dialogContext.CancelAllDialogsAsync(cancellationToken);
                    await turnContext.SendActivityAsync("Booking cancelled!", cancellationToken: cancellationToken);
                    await _botAccessors.ConversationState.SaveChangesAsync(turnContext, false, cancellationToken);
                    return;
                }

                var results = await dialogContext.ContinueDialogAsync(cancellationToken);
                var userInfo = results.Result as UserProfile;

                switch (results.Status)
                {
                    case DialogTurnStatus.Cancelled:
                    case DialogTurnStatus.Empty:
                        await _botAccessors.UserProfileAccessor.SetAsync(turnContext, new UserProfile(), cancellationToken);
                        await _botAccessors.UserState.SaveChangesAsync(turnContext, false, cancellationToken);
                        await dialogContext.BeginDialogAsync(Constants.RootDialog, null, cancellationToken);
                        break;
                    case DialogTurnStatus.Complete:
                        await _botAccessors.UserProfileAccessor.SetAsync(turnContext, userInfo, cancellationToken);
                        await _botAccessors.UserState.SaveChangesAsync(turnContext, false, cancellationToken);
                        if (!userInfo.FormCompleted)
                            await dialogContext.BeginDialogAsync(Constants.ReviewDialog, null, cancellationToken);
                        break;
                }

                await _botAccessors.ConversationState.SaveChangesAsync(turnContext, false, cancellationToken);
            }
            else if (turnContext.Activity.Type == ActivityTypes.ConversationUpdate)
            {
                if (turnContext.Activity.MembersAdded != null)
                {
                    await turnContext.SendActivityAsync(HelpText + "\n\nType anything to get started.", cancellationToken: cancellationToken);
                }
            }
        }

        private async Task<DialogTurnResult> NameStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            stepContext.Values[Constants.UserInfo] = new UserProfile();

            Activity prompt = MessageFactory.Text("Please enter your name.");

            return await stepContext.PromptAsync(
                Constants.NamePrompt,
                new PromptOptions { Prompt = prompt, RetryPrompt = prompt },
                cancellationToken);
        }

        private async Task<DialogTurnResult> AgeStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            ((UserProfile)stepContext.Values[Constants.UserInfo]).Name = (string)stepContext.Result;

            Activity prompt = MessageFactory.Text("Please enter your age.");

            return await stepContext.PromptAsync(
                Constants.AgePrompt,
                new PromptOptions { Prompt = prompt, RetryPrompt = prompt },
                cancellationToken);
        }

        private async Task<DialogTurnResult> RoomTypeSelectionStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            int age = (int)stepContext.Result;
            ((UserProfile)stepContext.Values[Constants.UserInfo]).Age = age;

            if (age < 18)
            {
                await stepContext.Context.SendActivityAsync(
                    MessageFactory.Text("You must be 18 or older to book a room."),
                    cancellationToken);
                return await stepContext.CancelAllDialogsAsync(cancellationToken);
            }
            else
            {
                return await stepContext.PromptAsync(Constants.RoomTypeSelectionPrompt,
                new PromptOptions
                {
                    Prompt = MessageFactory.Text("Choose your room type."),
                    Choices = ChoiceFactory.ToChoices(new List<string> { "Single", "Double", "Triple", "King" }),
                }, cancellationToken);
            }
        }

        private async Task<DialogTurnResult> PaymentTypeSelectionStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var roomType = ((FoundChoice)stepContext.Result).Value;
            ((UserProfile)stepContext.Values[Constants.UserInfo]).RoomType = roomType;

            return await stepContext.PromptAsync(Constants.RoomTypeSelectionPrompt,
                new PromptOptions
                {
                    Prompt = MessageFactory.Text("Choose your payment type."),
                    Choices = ChoiceFactory.ToChoices(new List<string> { "Money", "Credit card" }),
                }, cancellationToken);
        }

        private async Task<DialogTurnResult> FormCompletedStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var paymentType = ((FoundChoice)stepContext.Result).Value;
            ((UserProfile)stepContext.Values[Constants.UserInfo]).PaymentType = paymentType;

            return await stepContext.EndDialogAsync(stepContext.Values[Constants.UserInfo], cancellationToken: cancellationToken);

        }

        private async Task<DialogTurnResult> BookingConfirmationStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var userProfile = await _botAccessors.UserProfileAccessor.GetAsync(stepContext.Context, () => new UserProfile());
            stepContext.Values[Constants.UserInfo] = userProfile;

            var summary = $"**Name**: {userProfile.Name}\n\n" +
                          $"**Age**: {userProfile.Age}\n\n" +
                          $"**Room Type**: {userProfile.RoomType}\n\n" +
                          $"**Payment Type**: {userProfile.PaymentType}";

            return await stepContext.PromptAsync(Constants.ConfirmPrompt,
                new PromptOptions
                {
                    Prompt = MessageFactory.Text($"{summary}\n\nConfirm?"),
                    Choices = ChoiceFactory.ToChoices(new List<string> { "Yes", "No" }),
                }, cancellationToken);
        }

        private async Task<DialogTurnResult> FinishStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var confirmation = ((FoundChoice)stepContext.Result).Value;

            if (confirmation.Equals("Yes", StringComparison.InvariantCultureIgnoreCase))
                await stepContext.Context.SendActivityAsync("Booking completed. Thank you!");
            else
                await stepContext.Context.SendActivityAsync("Booking cancelled.");

            ((UserProfile)stepContext.Values[Constants.UserInfo]).FormCompleted = true;

            return await stepContext.EndDialogAsync(stepContext.Values[Constants.UserInfo], cancellationToken: cancellationToken);
        }

        private async Task<DialogTurnResult> OnHoldStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            string message = stepContext.Context.Activity.Text?.Trim();
            if (string.Equals(message, Constants.ContinueCommand, StringComparison.InvariantCultureIgnoreCase))
                return await stepContext.EndDialogAsync(null, cancellationToken);
            else
            {
                await stepContext.Context.SendActivityAsync(OnHoldText, cancellationToken: cancellationToken);
                return Dialog.EndOfTurn;
            }
        }

        private async Task<DialogTurnResult> ContinueToHoldStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken) =>
            await stepContext.ReplaceDialogAsync(Constants.OnHoldDialog, null, cancellationToken);

        public static string HelpText { get; } =
            "This bot helps you to booking a room.\n\n" +
            $" To pause the conversation at any time, enter `{Constants.WaitCommand}`.\n\n" +
            $" To resume the conversation, enter `{Constants.ContinueCommand}`.\n\n" +
            $" To cancel the conversation, enter `{Constants.CancelCommand}`.\n\n";

        private static string OnHoldText { get; } =
            "The conversation is on hold.\n\n" +
            $"Enter `{Constants.ContinueCommand}` to continue the conversation where you left off.";
    }
}
