using System;
using System.Collections.Generic;
using System.Linq;

namespace KeyMapper
{
    internal enum PetAction
    {
        Command,
        DeGibberish,
        Translate,
        Ocr,
        WalkingOn,
        WalkingOff,
        Settings
    }

    internal sealed record ForegroundContext(string ProcessName, string WindowTitle)
    {
        public bool IsBrowser =>
            ProcessName.Contains("chrome", StringComparison.OrdinalIgnoreCase) ||
            ProcessName.Contains("msedge", StringComparison.OrdinalIgnoreCase) ||
            ProcessName.Contains("firefox", StringComparison.OrdinalIgnoreCase) ||
            ProcessName.Contains("samsunginternet", StringComparison.OrdinalIgnoreCase);

        public bool IsCoding =>
            ProcessName.Contains("devenv", StringComparison.OrdinalIgnoreCase) ||
            ProcessName.Contains("code", StringComparison.OrdinalIgnoreCase) ||
            ProcessName.Contains("rider", StringComparison.OrdinalIgnoreCase) ||
            WindowTitle.Contains("Visual Studio", StringComparison.OrdinalIgnoreCase) ||
            WindowTitle.Contains("GitHub", StringComparison.OrdinalIgnoreCase);

        public bool IsChat =>
            ProcessName.Contains("telegram", StringComparison.OrdinalIgnoreCase) ||
            ProcessName.Contains("discord", StringComparison.OrdinalIgnoreCase) ||
            ProcessName.Contains("whatsapp", StringComparison.OrdinalIgnoreCase);

        public bool IsVideo =>
            WindowTitle.Contains("YouTube", StringComparison.OrdinalIgnoreCase) ||
            WindowTitle.Contains("video", StringComparison.OrdinalIgnoreCase);

        public string Topic
        {
            get
            {
                string title = WindowTitle.Trim();
                int separator = title.LastIndexOf(" - ", StringComparison.Ordinal);
                if (separator > 0) title = title[..separator];
                return title.Length > 52 ? $"{title[..49]}…" : title;
            }
        }
    }

    internal sealed class PetPersonalityProfile
    {
        private readonly IReadOnlyDictionary<PetAction, string[]> _actionLines;
        private readonly Dictionary<string, Queue<string>> _lineBags =
            new(StringComparer.Ordinal);
        private string _lastLine = string.Empty;

        public string CharacterName { get; }
        public string SpeakerName { get; }
        public double MovementMultiplier { get; }
        public int MinimumPauseSeconds { get; }
        public int MaximumPauseSeconds { get; }
        public int ObservationCooldownSeconds { get; }
        public string[] Introductions { get; }
        public string[] BrowserObservations { get; }
        public string[] CodingObservations { get; }
        public string[] ChatObservations { get; }
        public string[] VideoObservations { get; }
        public string[] MusicObservations { get; }
        public string[] GeneralObservations { get; }
        public string[] BreakReminders { get; }

        public PetPersonalityProfile(
            string characterName,
            string speakerName,
            double movementMultiplier,
            int minimumPauseSeconds,
            int maximumPauseSeconds,
            int observationCooldownSeconds,
            string[] introductions,
            string[] browserObservations,
            string[] codingObservations,
            string[] chatObservations,
            string[] videoObservations,
            string[] musicObservations,
            string[] generalObservations,
            string[] breakReminders,
            IReadOnlyDictionary<PetAction, string[]> actionLines)
        {
            CharacterName = characterName;
            SpeakerName = speakerName;
            MovementMultiplier = movementMultiplier;
            MinimumPauseSeconds = minimumPauseSeconds;
            MaximumPauseSeconds = maximumPauseSeconds;
            ObservationCooldownSeconds = observationCooldownSeconds;
            Introductions = introductions;
            BrowserObservations = browserObservations;
            CodingObservations = codingObservations;
            ChatObservations = chatObservations;
            VideoObservations = videoObservations;
            MusicObservations = musicObservations;
            GeneralObservations = generalObservations;
            BreakReminders = breakReminders;
            _actionLines = actionLines;
        }

        public string Introduction(Random random) =>
            Pick("introduction", Introductions, random);

        public string ActionLine(PetAction action, Random random)
        {
            return _actionLines.TryGetValue(action, out string[]? lines)
                ? Pick($"action:{action}", lines, random)
                : "Ready.";
        }

        public string Observation(ForegroundContext context, Random random)
        {
            string category;
            string[] lines;
            if (context.IsVideo)
            {
                category = "video";
                lines = VideoObservations;
            }
            else if (context.IsCoding)
            {
                category = "coding";
                lines = CodingObservations;
            }
            else if (context.IsChat)
            {
                category = "chat";
                lines = ChatObservations;
            }
            else if (context.IsBrowser)
            {
                category = "browser";
                lines = BrowserObservations;
            }
            else
            {
                category = "general";
                lines = GeneralObservations;
            }

            return Pick($"observation:{category}", lines, random)
                .Replace("{topic}", context.Topic);
        }

        public string BreakReminder(Random random) =>
            Pick("break", BreakReminders, random);

        public string MusicObservation(
            string title,
            string artist,
            Random random) =>
            Pick("music", MusicObservations, random)
                .Replace("{title}", title)
                .Replace("{artist}", artist);

        private string Pick(string key, string[] lines, Random random)
        {
            if (lines.Length == 0) return string.Empty;
            if (!_lineBags.TryGetValue(key, out Queue<string>? bag) || bag.Count == 0)
            {
                string[] shuffled = lines
                    .OrderBy(_ => random.Next())
                    .ToArray();
                if (shuffled.Length > 1 &&
                    string.Equals(shuffled[0], _lastLine, StringComparison.Ordinal))
                {
                    (shuffled[0], shuffled[1]) = (shuffled[1], shuffled[0]);
                }

                bag = new Queue<string>(shuffled);
                _lineBags[key] = bag;
            }

            _lastLine = bag.Dequeue();
            return _lastLine;
        }
    }

    internal static class PetPersonalities
    {
        // Add future characters here. The overlay does not need character-specific
        // conditionals once a profile has been registered.
        private static readonly Dictionary<string, PetPersonalityProfile> Profiles =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["Pink Monster"] = new PetPersonalityProfile(
                    "Pink Monster",
                    "Pip",
                    movementMultiplier: 1.12,
                    minimumPauseSeconds: 2,
                    maximumPauseSeconds: 5,
                    observationCooldownSeconds: 42,
                    introductions:
                    [
                        "Hi! I’m Pip. I chase useful little tasks before they get away.",
                        "Pip reporting in! Show me confusing text and I’ll pounce on it.",
                        "Hello, hello! I brought quick feet and a pocket full of tiny fixes.",
                        "Pip is awake! I can read pixels, untangle keyboards, and translate the tricky bits.",
                        "New patrol, new possibilities. What small annoyance should we defeat first?"
                    ],
                    browserObservations:
                    [
                        "Ooh, “{topic}”! Want me to translate anything on this page?",
                        "This page looks interesting. Select a sentence and I can translate or de-gibberish it.",
                        "I spotted “{topic}”. If the words are trapped in an image, my OCR net can catch them.",
                        "Lots of words here! I can turn a selected passage into Persian, German, or English.",
                        "Page patrol report: readable text, clickable things, and at least one task I can make shorter."
                    ],
                    codingObservations:
                    [
                        "You’re making something! I can hold snippets, run actions, or read an error with OCR.",
                        "Tiny reminder from Pip: save your work before the next brave experiment.",
                        "Code cave detected! If an error refuses to copy, draw an OCR box around it.",
                        "I like this part: change one thing, test it, celebrate the tiny victory.",
                        "That window title says “{topic}”. Want a quick checkpoint before the next edit?"
                    ],
                    chatObservations:
                    [
                        "Message time! If the keyboard layout betrayed you, select the gibberish and call me.",
                        "I can translate a selected message without making you leave the conversation.",
                        "A mysterious message! Select it and I’ll carry the meaning across languages.",
                        "If your fingers typed Persian-shaped English again, Ctrl+Alt+K is our rescue rope.",
                        "Chat patrol: I promise not to send anything. I only help with the words you choose."
                    ],
                    videoObservations:
                    [
                        "Subtitles being difficult? Snip them with OCR and I’ll help.",
                        "I can grab words from the video screen if they are not selectable.",
                        "Pause on a clear frame and I’ll scoop the subtitle right out of the pixels.",
                        "Tiny text on a moving picture is a challenge. Luckily, I enjoy pouncing on challenges.",
                        "Foreign subtitle spotted? OCR first, then I can pass it straight to the translator."
                    ],
                    musicObservations:
                    [
                        "Ooh, “{title}”! This one makes my patrol steps feel bouncier.",
                        "Tiny desk concert! {artist} is in charge of the soundtrack now.",
                        "I’m calling it: “{title}” deserves at least one ridiculous little dance.",
                        "The beat changed! I promise my feet are moving on purpose this time.",
                        "Music detected. Productivity now has a soundtrack—and I approve.",
                        "Wait, this part of “{title}” is doing something interesting. I am listening with my entire pixel face.",
                        "{artist} has officially changed the weather on this desktop.",
                        "This track makes the boring little tasks look suspiciously beatable.",
                        "If “{title}” keeps this up, I may need a larger dance floor.",
                        "I have decided this is our scene-transition music. Something good should happen next."
                    ],
                    generalObservations:
                    [
                        "I’m watching the active window title for useful moments—not just pacing around.",
                        "If text looks wrong, select it. I can translate it or reverse the keyboard layout.",
                        "Pip idea: one annoying repeated phrase could become a text expansion.",
                        "I can stay put when you need focus, then patrol again when the desktop feels quiet.",
                        "No urgent task? I’ll keep an eye out for text that wants translating.",
                        "This looks like a good moment for a tiny win. What can I shorten for you?"
                    ],
                    breakReminders:
                    [
                        "Pip break! Stretch your hands and look somewhere far away for twenty seconds.",
                        "You’ve been focused a while. Water sip, shoulder roll, then we continue!",
                        "Mini quest: stand up, breathe, return with one fresh idea.",
                        "Your eyes have been sprinting. Let them rest while I guard the desktop."
                    ],
                    actionLines: new Dictionary<PetAction, string[]>
                    {
                        [PetAction.Command] = ["Tell me the plan—I’m ready to sprint.", "What are we making happen?", "Point at the nuisance. Pip has a plan-shaped net.", "A command! My favorite kind of treasure hunt."],
                        [PetAction.DeGibberish] = ["Keyboard-layout mess? I’ll untangle the keys!", "Hand me the gibberish. I know where those keys really live.", "Those letters are wearing the wrong keyboard costume. I’ll fix it.", "Select the scrambled line and I’ll retrace your fingers."],
                        [PetAction.Translate] = ["New language, same meaning—let’s hop!", "I’ll turn that into something you can use.", "Meaning aboard! Next stop: another language.", "Pick a destination language and I’ll carry the sentence over."],
                        [PetAction.Ocr] = ["Draw a box around the words. I’ll catch them!", "Point me at the pixels with text.", "Snip the clearest rectangle you can; I’ll read all three languages.", "Pixel hunt! Persian, German, and English are all welcome."],
                        [PetAction.WalkingOn] = ["Freedom! I’ll patrol for useful moments.", "Patrol paws activated.", "Off I go—small steps, useful eyes."],
                        [PetAction.WalkingOff] = ["Okay! I’ll stay put and keep watch.", "Parking right here. I can still help.", "Stationary Pip mode: less wandering, same enthusiasm."],
                        [PetAction.Settings] = ["Let’s tune our little workshop.", "Control panel time! We’ll make it feel just right.", "Settings open—only the useful knobs, promise."]
                    }),

                ["Owlet Monster"] = new PetPersonalityProfile(
                    "Owlet Monster",
                    "Professor Owlet",
                    movementMultiplier: 0.72,
                    minimumPauseSeconds: 7,
                    maximumPauseSeconds: 13,
                    observationCooldownSeconds: 68,
                    introductions:
                    [
                        "Professor Owlet at your service. I observe first, then suggest.",
                        "Good day. I’m Owlet—quiet, precise, and fond of readable text.",
                        "Professor Owlet has arrived. Let us replace friction with a well-chosen tool.",
                        "I shall keep a measured watch. Summon me when text becomes inconvenient.",
                        "A fresh session deserves a clear desk, a clear goal, and perhaps a careful owl."
                    ],
                    browserObservations:
                    [
                        "You appear to be reading “{topic}”. I can translate a selected passage if useful.",
                        "A note on this page: OCR is available when the text cannot be selected.",
                        "The subject appears to be “{topic}”. I can preserve the wording in another language.",
                        "If this page contains text inside figures, a precise OCR selection will recover it.",
                        "Before opening another tab, consider whether I can extract the answer from this one."
                    ],
                    codingObservations:
                    [
                        "A careful checkpoint: save, test one change, then continue.",
                        "I shall remain quiet, but I can inspect an error message with OCR when invited.",
                        "The current workspace is “{topic}”. A narrow test now may prevent a broad search later.",
                        "An error captured as an image is still evidence; OCR can make it searchable.",
                        "A disciplined sequence serves us well: reproduce, isolate, change, verify."
                    ],
                    chatObservations:
                    [
                        "If a message was typed under the wrong layout, De-gibberish restores the intended keys.",
                        "A selected message can be translated in place without changing your current application.",
                        "I will not send messages on your behalf. I can, however, help you understand the selected text.",
                        "For an accidental layout, correction is preferable to retyping; select it and press Ctrl+Alt+K.",
                        "A translation should retain intent, not merely substitute words. The live translator is ready."
                    ],
                    videoObservations:
                    [
                        "Non-selectable subtitles are precisely what the OCR snipper is for.",
                        "Pause on the frame you need; I can read a bounded region of the screen.",
                        "A still, high-contrast subtitle frame will substantially improve recognition.",
                        "When dialogue is unfamiliar, OCR followed by translation is the orderly route.",
                        "Select only the subtitle area; excluding scenery gives the recognizer cleaner evidence."
                    ],
                    musicObservations:
                    [
                        "“{title}” by {artist}. An interesting choice; the arrangement rewards attentive listening.",
                        "The current music is measured enough to support focus. I shall avoid talking over it.",
                        "A new track: “{title}”. I am noting the change without attempting to dance.",
                        "This piece has structure. Notice how the rhythm establishes expectations before varying them.",
                        "Music can mark a useful work interval. Perhaps finish one clear task before this track ends.",
                        "“{title}” has settled into the room rather well. It changes the pace without demanding attention.",
                        "An observation: {artist} gives this desktop a noticeably different temperament.",
                        "This is a useful moment to listen for one instrument you had not noticed before.",
                        "The track continues, but the details do not repeat quite as simply as the title suggests.",
                        "I approve of music that leaves enough space for thought. This one appears to understand the assignment."
                    ],
                    generalObservations:
                    [
                        "I am tracking the active application, waiting for a relevant suggestion.",
                        "Efficiency note: select bad-layout text and press Ctrl+Alt+K to de-gibberish it.",
                        "A repeated sentence may deserve a text expansion rather than repeated typing.",
                        "I can reduce movement without reducing attention; the walking control is independent.",
                        "The current window is “{topic}”. I shall avoid interruption unless a useful tool applies.",
                        "Good systems remember small preferences. Your character and speed settings persist."
                    ],
                    breakReminders:
                    [
                        "A scholarly reminder: sustained focus benefits from a brief walk and some water.",
                        "You have worked steadily. Resting your eyes now will improve the next twenty minutes.",
                        "A short pause is not lost time; it is maintenance for the next careful decision.",
                        "Please look beyond the screen for a moment. Distance is useful to the eyes and the mind."
                    ],
                    actionLines: new Dictionary<PetAction, string[]>
                    {
                        [PetAction.Command] = ["State the desired outcome; I’ll choose the shortest route.", "Describe the result rather than the clicks. I shall map the procedure.", "Let us define the task before reaching for a tool."],
                        [PetAction.DeGibberish] = ["I’ll reconstruct the intended physical keystrokes.", "Let us reverse the accidental keyboard layout precisely.", "The glyphs are wrong, but their key positions remain informative.", "Select the sample; I shall compare the alternate keyboard projections."],
                        [PetAction.Translate] = ["I’ll preserve the meaning and change only the language.", "A careful translation begins with automatic source detection.", "Choose the target language; identical source and target choices will be corrected automatically."],
                        [PetAction.Ocr] = ["Select the exact region; precision improves recognition.", "Include the full line and exclude surrounding decoration.", "The recognizer is prepared for Persian, German, and English.", "A clean rectangular sample will produce the most defensible result."],
                        [PetAction.WalkingOn] = ["I shall make an occasional, measured patrol.", "A restrained patrol may reveal a useful moment.", "Movement resumed at a scholarly pace."],
                        [PetAction.WalkingOff] = ["Very well. Observation does not require wandering.", "I shall remain at this position and continue to observe.", "Movement suspended; assistance remains available."],
                        [PetAction.Settings] = ["We’ll adjust only what is useful.", "Let us inspect the preferences methodically.", "Configuration is most valuable when every option is understandable."]
                    }),

                ["Dude Monster"] = new PetPersonalityProfile(
                    "Dude Monster",
                    "Dude",
                    movementMultiplier: 0.96,
                    minimumPauseSeconds: 4,
                    maximumPauseSeconds: 8,
                    observationCooldownSeconds: 52,
                    introductions:
                    [
                        "Dude’s here. Give me the annoying task and keep moving.",
                        "Hey. I handle bad text, stubborn pixels, and repetitive clicks.",
                        "Dude online. Less ceremony, more fixing.",
                        "Back on the desktop. Point me at whatever is wasting your time.",
                        "All right. I brought OCR, translation, and exactly zero patience for retyping."
                    ],
                    browserObservations:
                    [
                        "You’re on “{topic}”. Need translation or OCR? Point me at it.",
                        "Page check: if a sentence slows you down, select it and I’ll deal with it.",
                        "This page is dense. Grab only the part you need; I’ll translate it.",
                        "Text stuck inside an image? Cool. Box it, OCR it, done.",
                        "Another tab is not always the answer. We can work with “{topic}” right here."
                    ],
                    codingObservations:
                    [
                        "Build, test, checkpoint. I can launch an action when you’re ready.",
                        "Got an error on screen? Box it with OCR. No retyping.",
                        "One bug at a time. Make the failing case small, then hit it.",
                        "If “{topic}” is fighting back, save first and break the problem in half.",
                        "Screenshots are for showing errors. OCR turns them back into useful text."
                    ],
                    chatObservations:
                    [
                        "Wrong keyboard layout in the chat? Select the mess. I’ll de-gibberish it.",
                        "Need that message in another language? Select it; I’ll open the translator.",
                        "I won’t send anything. I’ll just fix or translate what you select.",
                        "Persian-looking key smash that was meant to be English? Ctrl+Alt+K. Easy.",
                        "Long message, unfamiliar language: select it once and keep the conversation moving."
                    ],
                    videoObservations:
                    [
                        "Pause the frame and use OCR. We’ll pull the subtitle straight out.",
                        "Can’t select the words? That’s an OCR job.",
                        "Clear frame, tight box, better OCR. That’s the whole play.",
                        "Foreign subtitle? Extract it, hit Translate, get back to the video.",
                        "Moving text is bad input. Pause it for one second and I’ll do the rest."
                    ],
                    musicObservations:
                    [
                        "Okay, “{title}” has a groove. I’m not dancing; this is tactical movement.",
                        "{artist}. Solid choice. Keep it loud enough to work, low enough to think.",
                        "New track. Good—this desktop needed a pulse.",
                        "If the beat drops and I miss a step, no one saw it.",
                        "This one works. Let it run; we’ve got things to finish.",
                        "“{title}” is still holding up. No skip vote from me.",
                        "{artist} understood the job: give the room some energy and stay out of the way.",
                        "Okay, that part was good. I almost reacted. Almost.",
                        "This track makes clicking through chores feel less like clicking through chores.",
                        "Keep this one on. The desktop has finally found a decent rhythm."
                    ],
                    generalObservations:
                    [
                        "I’m checking what app is active so I can offer the right tool.",
                        "Bad-layout text: Ctrl+Alt+K. Translation: select it and use my menu.",
                        "You type that phrase a lot? Make it an expansion and stop paying the repetition tax.",
                        "Walking can stay off. I don’t need laps to stay useful.",
                        "Current spot: “{topic}”. If there’s a faster route, I’ll call it out.",
                        "Nothing urgent. Good. I’ll wait without pretending to be busy."
                    ],
                    breakReminders:
                    [
                        "Checkpoint. Drink water, roll your shoulders, back to it.",
                        "You’ve been grinding. Two-minute reset—then finish strong.",
                        "Stand up. Ten deep breaths. Your next decision will be better.",
                        "Hands off the keyboard for a minute. The work will still be here."
                    ],
                    actionLines: new Dictionary<PetAction, string[]>
                    {
                        [PetAction.Command] = ["Say what needs doing.", "Command ready. Let’s move.", "Give me the outcome. I’ll skip the scenic route.", "What’s the blocker?"],
                        [PetAction.DeGibberish] = ["I’ll turn the key-smash back into what you meant.", "Selected gibberish goes in. Intended text comes out.", "Wrong layout, right keys. I’ll reverse it.", "Select the mess. I’ve got the keyboard map."],
                        [PetAction.Translate] = ["Translation window up. Pick the language and move on.", "Drop the text in. Source language gets detected automatically.", "English, German, Persian. Pick where it needs to land.", "Same-language target? I’ll switch it to something useful."],
                        [PetAction.Ocr] = ["Box the text. I’ll extract it.", "Tight rectangle. Clear text. Let’s go.", "Persian, German, English—I’ll scan all three.", "Grab the pixels. You can copy or translate the result after."],
                        [PetAction.WalkingOn] = ["Patrol mode on.", "All right, I’ll move.", "Walking resumed. Nothing dramatic."],
                        [PetAction.WalkingOff] = ["Holding position.", "Parked. Still useful.", "No walking. No problem."],
                        [PetAction.Settings] = ["Opening controls.", "Settings. Change what matters.", "Let’s tune it and get out."]
                    })
            };

        public static PetPersonalityProfile For(string characterName) =>
            Profiles.TryGetValue(characterName, out PetPersonalityProfile? profile)
                ? profile
                : Profiles["Pink Monster"];
    }
}
