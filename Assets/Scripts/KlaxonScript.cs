using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class KlaxonScript : MonoBehaviour
{
	public KMAudio Audio;
	public KMBombInfo BombInfo;
	public KMBombModule BombModule;
	public KMBossModule BossModule;

	public TextMesh KlaxonText;
	public TextMesh ScratchworkText;

	public KMSelectable Buzzer;
	public KMSelectable LeftArrow;
	public KMSelectable RightArrow;

	public Renderer Surface;
	public TextMesh GuessText;

	public Material[] BuzzerOffOn;
	public Material[] ArrowOffOn;

	public Material[] BlankOrGuess;

	private static string[] DefaultIgnoredModules = new string[]
	{
		// Ignore other instances of The Klaxon
		"The Klaxon",
		// Bosses and semibosses
		"14",
		"42",
		"501",
		"Amnesia",
		"A>N<D",
		"Brainf---",
		"Busy Beaver",
		"Button Messer",
		"Cookie Jars",
		"Divided Squares",
		"Don't Touch Anything",
		"Encrypted Hangman",
		"Encryption Bingo",
		"Forget Any Color",
		"Forget Enigma",
		"Forget Everything",
		"Forget Infinity",
		"Forget It Not",
		"Forget Maze Not",
		"Forget Me Later",
		"Forget Me Not",
		"Forget Perspective",
		"Forget The Colors",
		"Forget Them All",
		"Forget This",
		"Forget Us Not",
		"Four-Card Monte",
		"Hogwarts",
		"Iconic",
		"Keypad Directionality",
		"Kugelblitz",
		"Mystery Module",
		"OmegaForget",
		"Organization",
		"Password Destroyer",
		"Purgatory",
		"RPS Judging",
		"Security Council",
		"Shoddy Chess",
		"Simon Forgets",
		"Simon's Stages",
		"Souvenir",
		"Tallordered Keys",
		"The Troll",
		"The Twin",
		"Übermodule",
		"Whiteout",
		// Pseudo-needies
		"Multitask",
		"Random Access Memory",
		"The Heart",
		"The Swan",
		"The Very Annoying Button",
		"Ultimate Custom Night",
		// Heavily time-dependent
		"Bamboozling Time Keeper",
		"The Time Keeper",
		"Timing is Everything",
		"Turn the Key",
	};

	private static Color[] FadeColors = Enumerable.Range(0, 61).Select(i => new Color(1, 1, 1, i / 60f)).ToArray();

	private int TextColorIndex = 60;
	private int TicksSinceKlaxonBecameIdle = 0;

	private const int BuzzerActuationTimeInTicks = 5;
	private const float MaxBuzzerDistance = 0.6f;
	private bool BuzzerIsPressed;
	private float BuzzerDistance;

	private char[] CorrectLetters;

	private bool KlaxonIsIdle;
	private int Score;

	// A lot of other boss modules use this trick. Maybe it helps so we don't query the list of solved modules on every FixedUpdate call? Either way, I might as well use it.
	private int SolveCheckTicks;
	private int TicksBuzzedIn;

	private Dictionary<string, int> SolvedModuleCounts;
	private int TotalModulesSolved;
	private Queue<string> QueuedKlaxonModules;

	private KtaneKlaxon.TextFitter TextFitter;

	private bool IsBuzzedIn;
	private bool CanBuzzIn;
	private bool IsSolved;

	static int ModuleIdCounter = 1;
	int ModuleId;

	void Awake()
	{
		ModuleId = ModuleIdCounter++;

		Buzzer.OnInteract += delegate ()
		{
			BuzzerIsPressed = true;
			Buzzer.AddInteractionPunch();

			if (IsBuzzedIn)
			{
				SubmitAnswer();
			}
			else
			{
				BuzzIn();
			}

			return false;
		};

		Buzzer.OnInteractEnded += delegate ()
		{
			BuzzerIsPressed = false;
		};

		LeftArrow.OnInteract += delegate ()
		{
			LeftArrow.AddInteractionPunch(0.5f);
			Audio.PlayGameSoundAtTransform(KMSoundOverride.SoundEffect.ButtonPress, transform);
			ChangeGuess(-1);
			return false;
		};

		RightArrow.OnInteract += delegate ()
		{
			RightArrow.AddInteractionPunch(0.5f);
			Audio.PlayGameSoundAtTransform(KMSoundOverride.SoundEffect.ButtonPress, transform);
			ChangeGuess(1);
			return false;
		};

		LeftArrow.OnInteractEnded += delegate()
		{
			Audio.PlayGameSoundAtTransform(KMSoundOverride.SoundEffect.ButtonRelease, transform);
		};

		RightArrow.OnInteractEnded += delegate()
		{
			Audio.PlayGameSoundAtTransform(KMSoundOverride.SoundEffect.ButtonRelease, transform);
		};

		TextFitter = new KtaneKlaxon.TextFitter(KlaxonText, ScratchworkText);
	}

	void Start()
	{
		SolveCheckTicks = 0;
		Score = 0;
		TicksBuzzedIn = 0;

		GuessText.text = "" + (char)('A' + Random.Range(0, 26));

		SolvedModuleCounts = new Dictionary<string, int>();
		TotalModulesSolved = 0;

		QueuedKlaxonModules = new Queue<string>();

		ModuleLog("Good evening and welcome to QI!");

		KlaxonIsIdle = true;
		IsSolved = false;

		CanBuzzIn = true;

		// Puzzle generation logic

		// Put the modules in an arbitrary order
		var ModulesInLogic = BombInfo.GetSolvableModuleNames().Select(name => name.ToUpperInvariant())
			.Except(BossModule.GetIgnoredModules(BombModule, DefaultIgnoredModules).Select(name => name.ToUpperInvariant()))
			.ToArray();

		ModuleLog("ModulesInLogic = [{0}]", ModulesInLogic.Join(", "));

		// For each letter, build up a sorted list of indices
		var alphabetSets = Enumerable.Range(0, 26).Select(i => new List<int>()).ToArray();
		for (int i = 0; i < ModulesInLogic.Length; i++)
		{
			var moduleLetters = ModulesInLogic[i].ToUpperInvariant().Intersect("ABCDEFGHIJKLMNOPQRSTUVWXYZ");
			foreach (char c in moduleLetters)
			{
				alphabetSets[c - 'A'].Add(i);
			}
		}

		// Turn the index lists into strings so we get key hashing for free
		var alphabetKeys = alphabetSets.Select(indices => indices.Select(i => i.ToString()).Join(",")).ToArray();

		var keysToCorrectChars = new Dictionary<string, List<char>>();
		for (int i = 0; i < alphabetKeys.Length; i++)
		{
			var key = alphabetKeys[i];
			if (!keysToCorrectChars.ContainsKey(key))
			{
				keysToCorrectChars[key] = new List<char>();
			}
			keysToCorrectChars[key].Add((char)('A' + i));
		}


		// 
		var singletons = keysToCorrectChars.Where(kvp => kvp.Value.Count() == 1);

		if (singletons.Any())
		{
			CorrectLetters = new[] {singletons.PickRandom().Value.First()};
			ModuleLog("Tonight's special letter is {0}. Any module you solve containing this letter will cause the klaxon to go off.", CorrectLetters.First());
		}
		else
		{
			CorrectLetters = keysToCorrectChars[alphabetKeys.PickRandom()].ToArray();
			var OxfordCommaIfNeeded = CorrectLetters.Length >= 3 ? "," : ""; // I like the Oxford comma. Don't judge me.
			ModuleLog("Tonight we have multiple special letters: {0}{1} and {2}, {3} of which produce the same set of klaxons. Any module you solve containing {4} of these letters will cause the klaxon to go off.", CorrectLetters.Take(CorrectLetters.Length - 1).Join(", "), OxfordCommaIfNeeded, CorrectLetters.Last(), CorrectLetters.Length >= 3 ? "all" : "both", CorrectLetters.Length >= 3 ? "any" : "either");
		}
	}
	
	void ModuleLog(string format, params object[] args)
	{
		var prefix = string.Format("[The Klaxon #{0}] ", ModuleId);
		Debug.LogFormat(prefix + format, args);
	}

	void FixedUpdate()
	{
		if (BuzzerIsPressed)
		{
			BuzzerDistance = Mathf.Min(MaxBuzzerDistance, BuzzerDistance + MaxBuzzerDistance / BuzzerActuationTimeInTicks);
		}
		else
		{
			BuzzerDistance = Mathf.Max(0, BuzzerDistance - MaxBuzzerDistance / BuzzerActuationTimeInTicks);
		}

		// Renderer.materials is weird :-/
		var materials = Buzzer.GetComponentInChildren<Renderer>().materials;
		materials[2] = BuzzerOffOn[IsBuzzedIn ? 1 : 0];
		Buzzer.GetComponentInChildren<Renderer>().materials = materials;

		Surface.material = BlankOrGuess[IsBuzzedIn ? 1 : 0];
		GuessText.color = IsBuzzedIn ? FadeColors.Last() : FadeColors.First();

		if (IsBuzzedIn)
		{
			TicksBuzzedIn++;
		}
		else
		{
			TicksBuzzedIn = 0;
		}

		LeftArrow.GetComponentInChildren<Renderer>().material = ArrowOffOn[(TicksBuzzedIn % 64) / 32];
		RightArrow.GetComponentInChildren<Renderer>().material = ArrowOffOn[(TicksBuzzedIn % 64) / 32];

		if (IsSolved)
		{
			IsBuzzedIn = false;
			return;
		}

		SolveCheckTicks++;
		if (SolveCheckTicks == 5)
		{
			SolveCheckTicks = 0;
			CheckForSolves();
		}

		if (KlaxonIsIdle && QueuedKlaxonModules.Any())
		{
			KlaxonIsIdle = false;
			StartCoroutine(Klaxon(QueuedKlaxonModules.Dequeue()));
		}

		Buzzer.transform.localPosition = BuzzerDistance * Vector3.down;

		if (KlaxonIsIdle)
		{
			TicksSinceKlaxonBecameIdle++;
			// Wait 3 seconds after the klaxon becomes idle before fading out the text
			if (TicksSinceKlaxonBecameIdle >= 150 && TextColorIndex > 0)
			{
				TextColorIndex--;
			}
			KlaxonText.color = FadeColors[IsBuzzedIn ? 0 : TextColorIndex];
		}
		else
		{
			TicksSinceKlaxonBecameIdle = 0;
			TextColorIndex = 60;
			if (IsBuzzedIn)
			{
				// Force the text to be off
				KlaxonText.color = FadeColors[0];
			}
		}
	}

	private void BuzzIn()
	{
		if (IsSolved || !CanBuzzIn)
		{
			return;
		}
		ModuleLog("Buzzed in...");
		int randomSound = Random.Range(1, 19); // 1 <= randomSound < 19
		Audio.PlaySoundAtTransform("buzzer" + randomSound.ToString(), transform);
		IsBuzzedIn = true;
	}

	private void ChangeGuess(int relative)
	{
		int charIndex = GuessText.text[0] - 'A' + relative;
		if (charIndex < 0)
		{
			charIndex += 26;
		}
		charIndex %= 26;
		GuessText.text = "" + (char)('A' + charIndex);
	}

	private void CheckForSolves()
	{
		if (IsSolved)
		{
			return;
		}

		var currentlySolvedModules = BombInfo.GetSolvedModuleNames();
		if (currentlySolvedModules.Count > TotalModulesSolved)
		{
			// We solved a new module
			// Build up the total counts of each module we solved
			var updatedSolveCounts = new Dictionary<string, int>();
			currentlySolvedModules.ForEach(module =>
			{
				if (!updatedSolveCounts.ContainsKey(module))
				{
					updatedSolveCounts[module] = 1;
				}
				else
				{
					updatedSolveCounts[module]++;
				}
			});

			var newlySolvedModules = updatedSolveCounts
				.ToDictionary(kvp => kvp.Key, kvp => kvp.Value - (SolvedModuleCounts.ContainsKey(kvp.Key) ? SolvedModuleCounts[kvp.Key] : 0))
				.SelectMany(kvp => Enumerable.Repeat(kvp.Key, kvp.Value)).ToArray();

			foreach (var module in newlySolvedModules)
			{
				ModuleLog("Detected new solve: \"{0}\"", module);
				if (IsBuzzedIn)
				{
					ModuleLog("Strike! You solved a module while buzzed in.");
					BombModule.HandleStrike();
					IsBuzzedIn = false;
				}
				if (module.ToUpperInvariant().Contains(CorrectLetters.First()))
				{
					QueuedKlaxonModules.Enqueue(module);
				}
			}

			SolvedModuleCounts = updatedSolveCounts;
			TotalModulesSolved += newlySolvedModules.Count();
		}
	}

	private void SubmitAnswer()
	{
		if (IsSolved)
		{
			return;
		}

		ModuleLog("Submitting {0}...", GuessText.text);
		char guessedChar = GuessText.text[0];
		if (CorrectLetters.Contains(guessedChar))
		{
			ModuleLog("Well done!");
			Audio.PlaySoundAtTransform("solve", transform);
			BombModule.HandlePass();
			IsSolved = true;
		}
		else
		{
			ModuleLog("Strike! That letter was incorrect.");
			BombModule.HandleStrike();
		}
		IsBuzzedIn = false;
	}

	private IEnumerator Klaxon(string phrase)
	{
		CanBuzzIn = false;
		ModuleLog("\"{0}\" triggered the klaxon! -10 points.", phrase);
		KlaxonText.color = Color.white;
		Score -= 10;
		ModuleLog("Your score: {0}", Score);

		// Low score record from https://qi.fandom.com/wiki/Differences
		// If this ever changes, this can probably be updated manually
		if (Score < -144)
		{
			ModuleLog("Congratulations! You've beaten Alan Davies' record for lowest score ever!");
		}

		TextFitter.FitText(phrase);

		// klaxon.ogg is a bit more than 6 seconds long, so we hang on to the klaxon for 7 seconds total
		Audio.PlaySoundAtTransform("klaxon", transform);
		for (int i = 0; i < 5; i++)
		{
			KlaxonText.color = FadeColors.Last();
			yield return new WaitForSeconds(0.55f);
			KlaxonText.color = FadeColors.First();
			yield return new WaitForSeconds(0.25f);
		}
		KlaxonText.color = FadeColors.Last();
		yield return new WaitForSeconds(2f);
		// The audio quiets down around 6 seconds in, so this is a good time when we can buzz in again
		CanBuzzIn = true;
		yield return new WaitForSeconds(1f);
		KlaxonIsIdle = true;
	}

	#pragma warning disable 414
	string TwitchHelpMessage = "Submit a letter with \"!{0} submit <letter>\" or \"!{0} <letter>\".";
	#pragma warning restore 414

	IEnumerator ProcessTwitchCommand(string command)
	{
		// Convert to uppercase because the stored character is uppercase
		var strippedCommand = command.ToUpperInvariant().Trim();
		if (!strippedCommand.RegexMatch("^(SUBMIT +)?[A-Z]$"))
		{
			yield break;
		}

		if (!CanBuzzIn)
		{
			yield return "sendtochat Sorry, you can't buzz in while the klaxon is sounding.";
			yield break;
		}

		yield return null;
		yield return new[] {Buzzer};
		yield return new WaitForSeconds(0.5f);

		char targetLetter = strippedCommand.Last();
		int distanceToTravel = targetLetter - GuessText.text[0];
		if (distanceToTravel < -13)
		{
			distanceToTravel += 26;
		}
		if (distanceToTravel > 13)
		{
			distanceToTravel -= 26;
		}

		if  (distanceToTravel != 0)
		{
			yield return Enumerable.Repeat(distanceToTravel > 0 ? RightArrow : LeftArrow, Mathf.Abs(distanceToTravel)).ToArray();
		}
		yield return new[] {Buzzer};
	}
}
