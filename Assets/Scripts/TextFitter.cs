using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace KtaneKlaxon
{
	struct Measured<T>
	{
		public readonly T Item;
		public readonly float Width;

		public Measured(T item, float width)
		{
			Item = item;
			Width = width;
		}
	}

	struct LineWrapNode
	{
		// The 0-based index of the last word in the previous line
		public readonly int BackPointer;

		public readonly float[] LineWidths;

		public readonly int[] WordCounts;

		public readonly float Cost;

		public static LineWrapNode Invalid = new LineWrapNode(0);

		private LineWrapNode(int _unused)
		{
			BackPointer = -1;
			LineWidths = null;
			WordCounts = null;
			Cost = float.PositiveInfinity;
		}

		public LineWrapNode(int backPointer, float[] lineWidths, int[] wordCounts, float minAcceptableWidth, float maxAcceptableWidth)
		{
			BackPointer = backPointer;
			LineWidths = lineWidths;
			WordCounts = wordCounts;

			// Cost function
			float maxLineWidth = lineWidths.Max();
			if (maxLineWidth > maxAcceptableWidth)
			{
				Cost = float.PositiveInfinity;
			}
			else
			{
				float smallestBoundary = Mathf.Min(maxLineWidth, minAcceptableWidth);
				// The cost is the fraction of empty space on each line squared, doubled with each additional line
				Cost = lineWidths.Sum(len => Mathf.Pow(1 - len / smallestBoundary, 2)) * Mathf.Pow(2, lineWidths.Length);
			}
		}
	}

	public class TextFitter
	{
		// Width of B plus 14 E's has a total advance of 1600 at 300 point font at character size 0.001
		// This seems like a good width to work with
		private const float MaxWidth = 1600;
		private const float SpaceWidth = 63;

		private const int MaximumOneLineFontSize = 300;
		private const int MinimumOneLineFontSize = 200;

		private const int MaximumMultiLineFontSize = 300;
		private const int MinimumMultiLineFontSize = 150;

		private TextMesh OutputMesh;
		private TextMesh ScratchworkMesh;

		public TextFitter(TextMesh output, TextMesh scratchwork)
		{
			OutputMesh = output;
			ScratchworkMesh = scratchwork;
		}

		public void FitText(string text)
		{
			ScratchworkMesh.text = text;

			var charWidths = new List<Measured<char>>();
			bool getCharacterInfoFailedAtLeastOnce = false;

			// Fetch character widths
			foreach (char c in text)
			{
				CharacterInfo info;
				if (ScratchworkMesh.font.GetCharacterInfo(c, out info, ScratchworkMesh.fontSize, ScratchworkMesh.fontStyle))
				{
					charWidths.Add(new Measured<char>(c, info.advance));
				}
				else
				{
					getCharacterInfoFailedAtLeastOnce = true;
					charWidths.Add(new Measured<char>(c, float.NaN));
				}
			}

			if (getCharacterInfoFailedAtLeastOnce)
			{
				if (charWidths.All(x => float.IsNaN(x.Width)))
				{
					// Not much we can do here except just guesstimate the widths.
					// 'W' has width 167 from experimental data, so this is a very safe metric to go by.
					// Hopefully we'll never actually have to resort to this, though.
					charWidths = text.Select(c => new Measured<char>(c, c == ' ' ? SpaceWidth : 167f)).ToList();
				}
				else
				{
					float average = charWidths.Select(x => x.Width).Where(f => !float.IsNaN(f)).Average();
					charWidths = charWidths.Select(x => float.IsNaN(x.Width) ? new Measured<char>(x.Item, average) : x).ToList();
				}
			}

			// Join the characters into words
			var words = new List<Measured<string>>();
			var currentWord = new StringBuilder();
			float currentLength = 0;
			foreach (var x in charWidths)
			{
				if (x.Item == ' ' && currentWord.Length > 0)
				{
					words.Add(new Measured<string>(currentWord.ToString(), currentLength));
					currentWord = new StringBuilder();
					currentLength = 0;
				}
				else if (x.Item != ' ')
				{
					currentWord.Append(x.Item);
					currentLength += x.Width;
				}
			}
			// Add the last word if it's there
			if (currentWord.Length > 0)
			{
				words.Add(new Measured<string>(currentWord.ToString(), currentLength));
			}

			// Check for a one-line solution
			float oneLineWidth = words.Sum(x => x.Width) + SpaceWidth * (words.Count - 1);
			//Debug.LogFormat("{0}", oneLineWidth);
			if (oneLineWidth <= MaxWidth)
			{
				OutputMesh.text = text;
				OutputMesh.fontSize = MaximumOneLineFontSize;
				return;
			}

			// Can we shrink the text a bit?
			if (oneLineWidth / MaxWidth < MaximumOneLineFontSize / (float)MinimumOneLineFontSize)
			{
				float ratio = oneLineWidth / MaxWidth;
				OutputMesh.text = text;
				OutputMesh.fontSize = (int)(MaximumOneLineFontSize / ratio);
				return;
			}

			// Just get rid of an annoying test case
			if (words.Count == 0)
			{
				OutputMesh.text = "";
				return;
			}

			// Look for multi-line solutions using dynamic programming
			float maxAcceptableLength = MaxWidth * MaximumMultiLineFontSize / (float)MinimumMultiLineFontSize;
			var nodes = new List<List<LineWrapNode>>();
			// Assemble the first line, which just consists of the costs of putting all the words on one line
			var firstList = new List<LineWrapNode>();
			firstList.Add(new LineWrapNode(-1, new[] { words[0].Width }, new[] { 1 }, MaxWidth, maxAcceptableLength));
			for (int i = 1; i < words.Count; i++)
			{
				firstList.Add(new LineWrapNode(-1, new[] { firstList.Last().LineWidths[0] + SpaceWidth + words[i].Width }, new[] { i + 1 }, MaxWidth, maxAcceptableLength));
			}
			nodes.Add(firstList);

			for (int numLineBreaks = 1; numLineBreaks < words.Count; numLineBreaks++)
			{
				// If there are N line breaks, there must be at least N+1 words
				var thisLayer = Enumerable.Repeat(LineWrapNode.Invalid, numLineBreaks).ToList();

				// Now, for each word, start scanning costs of adding blocks of words
				for (int lastWordIndex = numLineBreaks; lastWordIndex < words.Count; lastWordIndex++) // lastWordIndex is inclusive
				{
					LineWrapNode bestNode = LineWrapNode.Invalid;
					float widthForThisLine = -SpaceWidth;
					for (int firstWordIndex = lastWordIndex; firstWordIndex > numLineBreaks - 1; firstWordIndex--)
					{
						widthForThisLine += SpaceWidth + words[firstWordIndex].Width;
						// The last index of the previous line is firstWordIndex - 1
						var previousNode = nodes.Last()[firstWordIndex - 1];
						if (previousNode.LineWidths == null)
						{
							break;
						}
						var node = new LineWrapNode(firstWordIndex - 1, previousNode.LineWidths.Concat(new[] {widthForThisLine}).ToArray(), previousNode.WordCounts.Concat(new[] { lastWordIndex - firstWordIndex + 1 }).ToArray(), MaxWidth, maxAcceptableLength);
						if (node.Cost < bestNode.Cost)
						{
							bestNode = node;
						}
						else if (float.IsPositiveInfinity(node.Cost) && !float.IsPositiveInfinity(bestNode.Cost))
						{
							// This line is a lost cause, and we already have a decent solution
							break;
						}
					}
					thisLayer.Add(bestNode);
				}

				nodes.Add(thisLayer);
			}

			var bestOption = nodes.Select(list => list.Last()).OrderBy(node => node.Cost).First();

			var builder = new StringBuilder();
			int wordIndex = 0;
			foreach (int length in bestOption.WordCounts)
			{
				if (builder.Length > 0)
				{
					builder.Append("\n");
				}
				builder.Append(words.GetRange(wordIndex, length).Select(measured => measured.Item).Join(" "));
				wordIndex += length;
			}

			int fontSize = Mathf.Min(MaximumMultiLineFontSize, (int)(MaximumMultiLineFontSize * MaxWidth / bestOption.LineWidths.Max()));

			// TODO: Once we cover all cases, get rid of this line
			OutputMesh.fontSize = fontSize;
			OutputMesh.text = builder.ToString();
		}
	}
}