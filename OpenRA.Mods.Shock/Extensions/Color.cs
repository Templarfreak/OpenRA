using System;
using OpenRA;
using OpenRA.Primitives;

namespace Shock.Extensions
{
	static class Blending
	{
		/// <summary>
		/// Inputs 2 Colors, including alpha channel. Blends using formula from https://en.wikipedia.org/wiki/Alpha_compositing#Alpha_blending
		/// </summary>
		public static Color BlendRGB(this Color color, Color backColor)
		{
			int outR = 0;
			int outG = 0;
			int outB = 0;


			int outA = (color.A + backColor.A) * (1 - color.A);

			if (outA != 0)
			{
				outR = (((color.R * color.A) + (backColor.R * backColor.A)) * (1 - color.A)) / outA;
				outG = (((color.G * color.A) + (backColor.G * backColor.A)) * (1 - color.A)) / outA;
				outB = (((color.B * color.A) + (backColor.B * backColor.A)) * (1 - color.A)) / outA;
			}

			return Color.FromArgb(outR, outG, outB);
		}
	}
}
