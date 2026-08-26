using System;
using System.Collections.Generic;

namespace TenzoraX
{
    public class InputCaptureEngine
    {
        public bool IsCapturing { get; private set; }
        public List<string> CurrentCombo { get; } = new();
        public List<string> CurrentAction { get; } = new();

        /// <summary>Text-Aktion (inkl. Emojis). Wenn gesetzt, ersetzt sie die Action-Tasten.</summary>
        public string? CurrentActionText { get; private set; }

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
                // Tasten-Aktion ersetzt eine zuvor gesetzte Text-Aktion
                CurrentActionText = null;
                CurrentAction.Add(key);
                ActionChanged?.Invoke();
            }
        }

        /// <summary>Setzt die Text-Aktion; ersetzt die Action-Tasten, solange sie nicht leer ist.</summary>
        public void SetActionText(string? text)
        {
            text = string.IsNullOrWhiteSpace(text) ? null : text.Trim();
            CurrentActionText = text;
            if (text != null)
                CurrentAction.Clear();
            ActionChanged?.Invoke();
        }

        public void ClearAction()
        {
            CurrentAction.Clear();
            CurrentActionText = null;
            ActionChanged?.Invoke();
        }

        public void Reset()
        {
            IsCapturing = false;
            CurrentCombo.Clear();
            CurrentAction.Clear();
            CurrentActionText = null;
            CaptureStateChanged?.Invoke();
            ComboChanged?.Invoke();
            ActionChanged?.Invoke();
        }

        /// <summary>
        /// Lädt eine gespeicherte Kombination in den Editor, damit sie direkt
        /// bearbeitet werden kann (Text-Aktion/Tasten ändern, statt sie komplett
        /// neu aufzunehmen). Beendet eine laufende Aufnahme.
        /// </summary>
        public void LoadMapping(Mapping mapping)
        {
            IsCapturing = false;
            CurrentCombo.Clear();
            CurrentCombo.AddRange(mapping.Combo);
            CurrentAction.Clear();
            CurrentAction.AddRange(mapping.Action);
            CurrentActionText = mapping.Text;
            CaptureStateChanged?.Invoke();
            ComboChanged?.Invoke();
            ActionChanged?.Invoke();
        }

        public string ComboDisplay => string.Join(" + ", CurrentCombo);
        public string ActionDisplay => !string.IsNullOrEmpty(CurrentActionText) ? CurrentActionText : string.Join(" + ", CurrentAction);
        public bool HasCombo => CurrentCombo.Count > 0;
        public bool HasAction => CurrentAction.Count > 0 || !string.IsNullOrEmpty(CurrentActionText);
    }
}
