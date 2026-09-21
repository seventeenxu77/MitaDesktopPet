using System;

namespace DesktopPet
{
    public interface IChatBackend
    {
        bool IsBusy { get; }

        void Send(string userText, Action<string> onCompleted, Action<string> onError);
        void Cancel();
        void ResetConversation();
    }

    // A backend may prepare a separate spoken version of the visible reply.
    public interface ILocalizedChatBackend
    {
        string LastSpeechText { get; }
    }
}
