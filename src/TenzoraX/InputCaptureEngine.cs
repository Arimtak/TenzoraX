using System;
using System.Collections.Generic;

namespace TenzoraX
{
    public class InputCaptureEngine
    {
        public bool IsCapturing { get; private set; }
        public List<string> CurrentCombo { get; } = new();
        public List<string> CurrentAction { get; } = new();

        public event Action? CaptureStateChanged;
        public event Action? ComboChanged;
        public event Action? ActionChanged;

        public void StartCapture()
        {
            if (IsCapturing) return;
            IsCapturing = true;
            CurrentCombo.Clear();
            CaptureStateChanged?.Invoke();
            ComboChanged?.Invoke();
        }

        public void StopCapture()
        {
            if (!IsCapturing) return;
            IsCapturing = false;
            CaptureStateChanged?.Invoke();
        }

        public void ToggleCapture()
        {
            if (IsCapturing)
                StopCapture();
            else
                StartCapture();
        }

        public void AddComboButton(string button)
        {
            if (!IsCapturing) return;
            if (!CurrentCombo.Contains(button))
            {
                CurrentCombo.Add(button);
                ComboChanged?.Invoke();
            }
        }

        public void RemoveComboButton(string button)
        {
            if (CurrentCombo.Remove(button))
                ComboChanged?.Invoke();
        }

        public void ClearCombo()
        {
            CurrentCombo.Clear();
            ComboChanged?.Invoke();
        }

        public void AddActionKey(string key)
        {
            if (!CurrentAction.Contains(key))
            {
                CurrentAction.Add(key);
                ActionChanged?.Invoke();
            }
        }

        public void ClearAction()
        {
            CurrentAction.Clear();
            ActionChanged?.Invoke();
        }

        public void Reset()
        {
            IsCapturing = false;
            CurrentCombo.Clear();
            CurrentAction.Clear();
            CaptureStateChanged?.Invoke();
            ComboChanged?.Invoke();
            ActionChanged?.Invoke();
        }

        public string ComboDisplay => string.Join(" + ", CurrentCombo);
        public string ActionDisplay => string.Join(" + ", CurrentAction);
        public bool HasCombo => CurrentCombo.Count > 0;
        public bool HasAction => CurrentAction.Count > 0;
    }
}
