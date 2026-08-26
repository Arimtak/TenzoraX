using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Xunit;

namespace TenzoraX.Tests
{
    /// <summary>
    /// Testet das Combo-Matching des ProfileManager. Über die internen Test-Nähte
    /// (ProfileDirOverride + KeyOutputOverride) wird in einem Temp-Verzeichnis
    /// gearbeitet und die Ausgabe abgefangen – es werden keine echten Tastatureingaben
    /// (SendInput) und kein Sound erzeugt.
    /// </summary>
    public class ProfileManagerTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly List<OutputEvent> _events = new();

        private sealed record OutputEvent(List<string> Keys, bool IsDown);

        public ProfileManagerTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "TenzoraXTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);

            ProfileManager.ProfileDirOverride = _tempDir;
            ProfileManager.KeyOutputOverride = (keys, isDown) =>
            {
                // Thread-sicher: der Stress-Test feuert KeyDown/KeyUp aus mehreren Threads
                lock (_events)
                    _events.Add(new OutputEvent(keys.ToList(), isDown));
            };
            ProfileManager.TextOutputOverride = null;

            // Produktionsseiteneffekte in Tests ausstellen
            SoundManager.Enabled = false;
            NotificationManager.Enabled = false;
            ProfileManager.SetPaused(false);

            // Singleton-Zustand zwischen Tests isolieren
            ProfileManager.Instance.ResetStateForTesting();
        }

        public void Dispose()
        {
            ProfileManager.KeyOutputOverride = null;
            ProfileManager.TextOutputOverride = null;
            ProfileManager.ProfileDirOverride = null;
            try { Directory.Delete(_tempDir, true); } catch { }
        }

        private void SetProfile(params (string[] Combo, string[] Action)[] mappings)
        {
            ProfileManager.Instance.ActiveProfile = new MappingProfile
            {
                Name = "Test",
                Mappings = mappings.Select(m => new Mapping
                {
                    Combo = m.Combo.ToList(),
                    Action = m.Action.ToList()
                }).ToList()
            };
        }

        private OutputEvent? Last => _events.Count > 0 ? _events[^1] : null;

        [Fact]
        public void PressCombo_TriggersKeyDown()
        {
            SetProfile((new[] { "L1", "SELECT" }, new[] { "F13" }));

            ProfileManager.Instance.ProcessControllerInput(new List<string> { "L1", "SELECT" });

            Assert.Single(_events);
            Assert.False(Last!.IsDown);
            Assert.Equal(new[] { "F13" }, Last.Keys);
        }

        [Fact]
        public void Release_TriggersKeyUp()
        {
            SetProfile((new[] { "L1", "SELECT" }, new[] { "F13" }));

            ProfileManager.Instance.ProcessControllerInput(new List<string> { "L1", "SELECT" });
            ProfileManager.Instance.ProcessControllerInput(new List<string>());

            Assert.Equal(2, _events.Count);
            Assert.True(_events[1].IsDown);
            Assert.Equal(new[] { "F13" }, _events[1].Keys);
        }

        [Fact]
        public void HoldingCombo_TriggersOnlyOnce()
        {
            SetProfile((new[] { "L1", "SELECT" }, new[] { "F13" }));

            var pressed = new List<string> { "L1", "SELECT" };
            ProfileManager.Instance.ProcessControllerInput(pressed);
            ProfileManager.Instance.ProcessControllerInput(pressed);
            ProfileManager.Instance.ProcessControllerInput(pressed);

            Assert.Single(_events);
            Assert.False(_events[0].IsDown);
        }

        [Fact]
        public void PartialCombo_DoesNotTrigger()
        {
            SetProfile((new[] { "L1", "SELECT" }, new[] { "F13" }));

            ProfileManager.Instance.ProcessControllerInput(new List<string> { "L1" });

            Assert.Empty(_events);
        }

        [Fact]
        public void MultipleMappings_OnlyMatchingComboTriggers()
        {
            SetProfile(
                (new[] { "A" }, new[] { "F13" }),
                (new[] { "B", "X" }, new[] { "SHIFT", "TAB" }));

            ProfileManager.Instance.ProcessControllerInput(new List<string> { "B", "X" });

            Assert.Single(_events);
            Assert.Equal(new[] { "SHIFT", "TAB" }, Last!.Keys);
        }

        [Fact]
        public void EmptyComboMapping_IsIgnored()
        {
            SetProfile((Array.Empty<string>(), new[] { "F13" }));

            ProfileManager.Instance.ProcessControllerInput(new List<string>());

            Assert.Empty(_events);
        }

        [Fact]
        public void Paused_DoesNotTrigger()
        {
            SetProfile((new[] { "L1", "SELECT" }, new[] { "F13" }));
            ProfileManager.SetPaused(true);

            ProfileManager.Instance.ProcessControllerInput(new List<string> { "L1", "SELECT" });

            Assert.Empty(_events);
            ProfileManager.SetPaused(false);
        }

        [Fact]
        public void Pause_ReleasesHeldKeys()
        {
            SetProfile((new[] { "L1", "SELECT" }, new[] { "F13" }));

            ProfileManager.Instance.ProcessControllerInput(new List<string> { "L1", "SELECT" });
            Assert.Single(_events); // KeyDown

            ProfileManager.SetPaused(true);
            Assert.Equal(2, _events.Count);  // KeyUp beim Pausieren
            Assert.True(_events[1].IsDown);
            Assert.Equal(new[] { "F13" }, _events[1].Keys);

            // Solange pausiert ist, darf nichts mehr ausgelöst werden
            ProfileManager.Instance.ProcessControllerInput(new List<string> { "L1", "SELECT" });
            Assert.Equal(2, _events.Count);

            ProfileManager.SetPaused(false);
        }

        [Fact]
        public void Unpause_WithButtonsStillHeld_RefiresMapping()
        {
            SetProfile((new[] { "L1", "SELECT" }, new[] { "F13" }));

            ProfileManager.Instance.ProcessControllerInput(new List<string> { "L1", "SELECT" });  // KeyDown
            ProfileManager.SetPaused(true);                                                         // KeyUp
            ProfileManager.SetPaused(false);

            // Noch gehalten → Mapping feuert erneut (wie ein frisches Press-Event)
            ProfileManager.Instance.ProcessControllerInput(new List<string> { "L1", "SELECT" });  // KeyDown
            Assert.Equal(3, _events.Count);
            Assert.False(_events[2].IsDown);

            // Loslassen → KeyUp
            ProfileManager.Instance.ProcessControllerInput(new List<string>());
            Assert.Equal(4, _events.Count);
            Assert.True(_events[3].IsDown);
        }

        [Fact]
        public void ProfileSwitch_ReleasesHeldKeys()
        {
            SetProfile((new[] { "L1", "SELECT" }, new[] { "F13" }));

            ProfileManager.Instance.ProcessControllerInput(new List<string> { "L1", "SELECT" });
            Assert.Single(_events); // KeyDown

            // Neues Profil, das die gehaltene Combo nicht mehr kennt
            ProfileManager.Instance.SaveProfile(new MappingProfile
            {
                Name = "Other",
                Mappings = new List<Mapping>
                {
                    new Mapping { Combo = new List<string> { "A" }, Action = new List<string> { "F14" } }
                }
            });
            ProfileManager.Instance.LoadProfileByName("Other");

            // Profilwechsel muss die gehaltene Taste freigeben, statt sie hängen zu lassen
            Assert.Equal(2, _events.Count);
            Assert.True(_events[1].IsDown);
            Assert.Equal(new[] { "F13" }, _events[1].Keys);

            // Neue Combo im neuen Profil löst normal aus
            ProfileManager.Instance.ProcessControllerInput(new List<string> { "A" });
            Assert.Equal(3, _events.Count);
            Assert.False(_events[2].IsDown);
        }

        [Fact]
        public async Task ConcurrentProcessing_WithEditsAndPause_IsThreadSafe()
        {
            SetProfile((new[] { "A" }, new[] { "F13" }), (new[] { "B" }, new[] { "F14" }));

            var pingPong = new Mapping
            {
                Combo = new List<string> { "X" },
                Action = new List<string> { "F20" }
            };
            var errors = 0;

            // Simuliert den Poll-Thread (ProcessControllerInput) parallel zu
            // UI-Thread-Aktivitäten (Mapping bearbeiten, Pause umschalten).
            var processor = Task.Run(() =>
            {
                for (int i = 0; i < 4000; i++)
                {
                    var pressed = (i & 1) == 0
                        ? new List<string> { "A" }
                        : new List<string> { "B" };
                    try
                    {
                        ProfileManager.Instance.ProcessControllerInput(pressed);
                    }
                    catch (Exception)
                    {
                        Interlocked.Increment(ref errors);
                    }
                }
            });

            var editor = Task.Run(() =>
            {
                for (int i = 0; i < 2000; i++)
                {
                    ProfileManager.Instance.AddMapping(pingPong);
                    ProfileManager.Instance.RemoveMapping(pingPong);
                }
            });

            var pauser = Task.Run(() =>
            {
                for (int i = 0; i < 1000; i++)
                    ProfileManager.SetPaused((i & 1) == 0);
                ProfileManager.SetPaused(false);
            });

            await Task.WhenAll(processor, editor, pauser);

            ProfileManager.Instance.ResetStateForTesting();
            Assert.Equal(0, errors);
        }

        [Fact]
        public void TextAction_TriggersOnce_AndDoesNotRelease()
        {
            ProfileManager.Instance.ActiveProfile = new MappingProfile
            {
                Name = "Text",
                Mappings = new List<Mapping>
                {
                    new Mapping { Combo = new List<string> { "LB" }, Text = "🎉 gg" }
                }
            };

            string? typed = null;
            ProfileManager.TextOutputOverride = t => typed = t;

            try
            {
                ProfileManager.Instance.ProcessControllerInput(new List<string> { "LB" });
                Assert.Equal("🎉 gg", typed);

                // Solange gehalten: kein zweites Tippen
                ProfileManager.Instance.ProcessControllerInput(new List<string> { "LB" });
                Assert.Equal("🎉 gg", typed);

                // Loslassen: keine erneute Ausgabe (kein KeyUp/Re-Trigger)
                ProfileManager.Instance.ProcessControllerInput(new List<string>());
                Assert.Equal("🎉 gg", typed);

                // Erneutes Drücken tippt wieder
                ProfileManager.Instance.ProcessControllerInput(new List<string> { "LB" });
                Assert.Equal("🎉 gg", typed);
            }
            finally
            {
                ProfileManager.TextOutputOverride = null;
            }
        }

        [Fact]
        public void TextAction_DisplayShowsText_NotButtonLayout()
        {
            var m = new Mapping { Combo = new List<string> { "LB" }, Text = "🎉" };

            Assert.True(m.HasTextAction);
            Assert.Equal("🎉", m.DisplayAction);
            Assert.Single(m.ActionParts);
            Assert.Equal("🎉", m.ActionParts[0].Text);
            Assert.False(m.ActionParts[0].IsButton);
        }

        [Fact]
        public void TriggerPath_IsSynchronousOnCallingThread_AndRecordsLatency()
        {
            // Latenz-Regression: Die Ausgabe muss synchron auf dem aufrufenden
            // (Poll-)Thread passieren und den Latenz-Monitor befüllen. Wenn jemand
            // wieder Task.Run oder einen Dispatcher-Umweg in den Trigger-Pfad
            // einbaut, läuft die Ausgabe auf einem anderen Thread bzw. nach
            // Rückkehr – die Assertions schlagen dann fehl.
            LatencyMonitor.ResetForTesting();
            ProfileManager.Instance.ActiveProfile = new MappingProfile
            {
                Name = "Latency",
                Mappings = new List<Mapping>
                {
                    new Mapping { Combo = new List<string> { "LB" }, Action = new List<string> { "F13" } }
                }
            };

            int callerThread = Thread.CurrentThread.ManagedThreadId;
            int? outputThread = null;
            bool outputRan = false;
            ProfileManager.KeyOutputOverride = (keys, isDown) =>
            {
                outputThread = Thread.CurrentThread.ManagedThreadId;
                outputRan = true;
            };

            try
            {
                LatencyMonitor.MarkInputEvent();
                var sw = Stopwatch.StartNew();
                ProfileManager.Instance.ProcessControllerInput(new List<string> { "LB" });
                sw.Stop();

                // Ausgabe lief synchron auf dem Aufrufer-Thread und VOR Rückkehr
                Assert.True(outputRan, "Ausgabe muss innerhalb von ProcessControllerInput laufen");
                Assert.Equal(callerThread, outputThread);

                // Grober Smoke-Check: kein Sleep/Async-Hop im Trigger-Pfad
                Assert.True(sw.Elapsed < TimeSpan.FromMilliseconds(100),
                    $"Trigger-Pfad dauert unerwartet lange: {sw.Elapsed.TotalMilliseconds:F1} ms");

                // Der echte Trigger-Pfad muss den Latenz-Monitor befüllt haben
                // (MarkInputEvent → ProcessControllerInput → RecordOutputDone).
                string firstLine = LatencyMonitor.FormatSummary().Split('\n')[0];
                Assert.Contains("Eingabe→SendInput", firstLine);
                Assert.DoesNotContain("noch keine", firstLine);
            }
            finally
            {
                ProfileManager.KeyOutputOverride = null;
            }
        }

        [Fact]
        public void Name_DisplayShowsActionKeysAndName()
        {
            // Benannte Kombination mit Ausgabe-Tasten: Anzeige = Tasten ➔ Name.
            var m = new Mapping
            {
                Combo = new List<string> { "LB" },
                Action = new List<string> { "F13" },
                Name = "Steam Overlay"
            };

            Assert.True(m.HasName);
            // Notification: „F13 ➔ Steam Overlay“ (der Combo-Teil kommt im Fenster dazu)
            Assert.Equal("F13 ➔ Steam Overlay", m.DisplayLabel);
            // Liste: Ausgabe-Tasten als Boxen, der Name separat dahinter
            Assert.Single(m.ActionParts);
            Assert.Equal("F13", m.ActionParts[0].Text);
            Assert.True(m.ActionParts[0].IsButton);
            Assert.Equal("Steam Overlay", m.ActionName);
            Assert.True(m.HasActionOutput);
            Assert.True(m.ShowNameArrow);
        }

        [Fact]
        public void Name_DisplayShowsTextActionAndName()
        {
            // Benannte Kombination mit Text-Aktion: die Text-Aktion ist die Ausgabe
            // (wird getippt, Tasten ignoriert) → Anzeige = Text ➔ Name.
            var m = new Mapping
            {
                Combo = new List<string> { "LB" },
                Action = new List<string> { "F13" },
                Text = "Emoji",
                Name = "Benannt"
            };

            Assert.True(m.HasName);
            Assert.Equal("Emoji ➔ Benannt", m.DisplayLabel);
            Assert.Single(m.ActionParts);
            Assert.Equal("Emoji", m.ActionParts[0].Text);
            Assert.False(m.ActionParts[0].IsButton);
            Assert.Equal("Benannt", m.ActionName);
            Assert.True(m.HasActionOutput);
            Assert.True(m.ShowNameArrow);
        }

        [Fact]
        public void Name_DisplayShowsNameOnly_WithoutActionKeys()
        {
            // Benannte Kombination OHNE Ausgabe-Tasten: nur der Name wird angezeigt.
            var m = new Mapping
            {
                Combo = new List<string> { "LB" },
                Name = "test"
            };

            Assert.True(m.HasName);
            Assert.Equal("test", m.DisplayLabel);
            // Keine Ausgabe-Tasten → ActionParts leer, Name separat
            Assert.Empty(m.ActionParts);
            Assert.Equal("test", m.ActionName);
            Assert.False(m.HasActionOutput);
            Assert.False(m.ShowNameArrow);
        }

        [Fact]
        public void Name_IsNeverTyped_OnlyKeysTrigger()
        {
            // Benannte Tasten-Kombination: der Name ist NUR eine Beschriftung,
            // beim Auslösen werden die Action-Tasten gesendet, der Name nie getippt.
            ProfileManager.Instance.ActiveProfile = new MappingProfile
            {
                Name = "Named",
                Mappings = new List<Mapping>
                {
                    new Mapping { Combo = new List<string> { "LB" }, Action = new List<string> { "F13" }, Name = "Sound" }
                }
            };

            var keys = new List<(List<string> k, bool up)>();
            string? typed = null;
            ProfileManager.KeyOutputOverride = (k, up) => keys.Add((new List<string>(k), up));
            ProfileManager.TextOutputOverride = t => typed = t;

            try
            {
                ProfileManager.Instance.ProcessControllerInput(new List<string> { "LB" });
                // Der Name ist nur eine Beschriftung – es wird NIE Text getippt,
                // sondern nur die Action-Taste ausgelöst.
                Assert.Null(typed);
                Assert.Single(keys);
                Assert.Equal(new List<string> { "F13" }, keys[0].k);
                Assert.False(keys[0].up);
            }
            finally
            {
                ProfileManager.KeyOutputOverride = null;
                ProfileManager.TextOutputOverride = null;
            }
        }

        [Fact]
        public void Name_WithoutName_FallsBackToTextAction()
        {
            // Ohne Namen gilt weiterhin die Text-Aktion als Anzeige
            var m = new Mapping { Combo = new List<string> { "A" }, Text = "🎉" };
            Assert.False(m.HasName);
            Assert.Equal("🎉", m.DisplayLabel);
        }

        [Fact]
        public void Name_SurvivesProfileRoundTrip()
        {
            ProfileManager.Instance.SaveProfile(new MappingProfile
            {
                Name = "NameRoundTrip",
                Mappings = new List<Mapping>
                {
                    new Mapping { Combo = new List<string> { "A" }, Action = new List<string> { "F13" }, Name = "Steam Overlay" }
                }
            });

            var loaded = ProfileManager.Instance.LoadProfileByName("NameRoundTrip");
            Assert.NotNull(loaded);
            Assert.Equal("Steam Overlay", loaded!.Mappings[0].Name);
        }

        [Fact]
        public void TextAction_SurvivesProfileRoundTrip()
        {
            ProfileManager.Instance.SaveProfile(new MappingProfile
            {
                Name = "TextRoundTrip",
                Mappings = new List<Mapping>
                {
                    new Mapping { Combo = new List<string> { "A" }, Text = "hi 👋" }
                }
            });

            var loaded = ProfileManager.Instance.LoadProfileByName("TextRoundTrip");
            Assert.NotNull(loaded);
            Assert.Equal("hi 👋", loaded!.Mappings[0].Text);
        }
    }
}
