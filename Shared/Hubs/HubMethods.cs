namespace Shared.Hubs;

public static class HubMethods
{
    public static class Chat
    {
        public const string ReceiveMessage = "ReceiveMessageDto";
        public const string MessageUpdated = "MessageUpdated";
        public const string MessageDeleted = "MessageDeleted";
        public const string PollUpdated = "ReceivePollUpdate";

        public const string MessageRead = "MessageRead";
        public const string UnreadCountUpdated = "UnreadCountUpdated";

        public const string UserOnline = "UserOnline";
        public const string UserOffline = "UserOffline";
        public const string UserStatusChanged = "UserStatusChanged";
        public const string UserTyping = "UserTyping";

        public const string ChatUpdated = "ChatUpdated";
        public const string MemberJoined = "MemberJoined";
        public const string MemberLeft = "MemberLeft";

        public const string ReceiveNotification = "ReceiveNotification";

        public const string UserProfileUpdated = "UserProfileUpdated";
    }

    public static class ChatInvoke
    {
        public const string JoinChat = "JoinChat";
        public const string LeaveChat = "LeaveChat";
        public const string MarkAsRead = "MarkAsRead";
        public const string MarkMessageAsRead = "MarkMessageAsRead";
        public const string SendTyping = "SendTyping";
        public const string GetOnlineUsers = "GetOnlineUsersInChat";
        public const string SetStatus = "SetStatus";
        public const string GetUnreadCounts = "GetUnreadCounts";
        public const string GetReadInfo = "GetReadInfo";
    }

    public static class Call
    {
        public const string IncomingCall = "IncomingCall";
        public const string CallStateUpdated = "CallStateUpdated";
        public const string CallEnded = "CallEnded";
        public const string CallError = "CallError";
        public const string CallMessageReceived = "CallMessageReceived";
        public const string CallParticipantJoined = "CallParticipantJoined";
        public const string CallParticipantLeft = "CallParticipantLeft";
        public const string ParticipantMuteChanged = "ParticipantMuteChanged";
        public const string ParticipantSpeakingChanged = "ParticipantSpeakingChanged";
        public const string ActiveCallStarted = "ActiveCallStarted";
        public const string ActiveCallUpdated = "ActiveCallUpdated";
        public const string ActiveCallEnded = "ActiveCallEnded";
        public const string ReceiveSignal = "ReceiveSignal";
    }

    public static class CallInvoke
    {
        public const string InitiateCall = "InitiateCall";
        public const string JoinCall = "JoinCall";
        public const string LeaveCall = "LeaveCall";
        public const string DeclineCall = "DeclineCall";
        public const string CancelCall = "CancelCall";
        public const string SendSignal = "SendSignal";
        public const string ToggleMute = "ToggleMute";
        public const string ToggleSpeaking = "ToggleSpeaking";
        public const string SendCallMessage = "SendCallMessage";
        public const string GetCallState = "GetCallState";
    }
}