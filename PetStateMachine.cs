using System;

namespace KeyMapper
{
    public enum PetState
    {
        Idle,
        Sleeping,
        Listening,
        Working,
        Alerting,
        Talking
    }

    public class PetStateChangedEventArgs : EventArgs
    {
        public PetState OldState { get; }
        public PetState NewState { get; }
        public string? StatusMessage { get; }

        public PetStateChangedEventArgs(PetState oldState, PetState newState, string? statusMessage = null)
        {
            OldState = oldState;
            NewState = newState;
            StatusMessage = statusMessage;
        }
    }

    public class PetStateMachine
    {
        public PetState CurrentState { get; private set; } = PetState.Idle;

        public event EventHandler<PetStateChangedEventArgs>? StateChanged;

        public void SetState(PetState newState, string? message = null)
        {
            if (CurrentState == newState && message == null)
                return;

            PetState oldState = CurrentState;
            CurrentState = newState;

            StateChanged?.Invoke(this, new PetStateChangedEventArgs(oldState, newState, message));
        }

        public string GetStateDescriptor()
        {
            return CurrentState switch
            {
                PetState.Idle => "Gentleman Pet is resting on your desktop.",
                PetState.Sleeping => "Zzz... Pet is asleep.",
                PetState.Listening => "Listening for shortcuts & commands...",
                PetState.Working => "Processing task...",
                PetState.Alerting => "Attention needed!",
                PetState.Talking => "Speaking...",
                _ => "Active"
            };
        }
    }
}
