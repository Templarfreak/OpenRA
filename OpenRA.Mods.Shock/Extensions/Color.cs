using System;
using System.Drawing;
using OpenRA;

namespace Shock.Extensions
{
	static class Blending
	{
		/// <summary>
		/// Inputs 2 Colors. Blends using 
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

		/// <summary>
		/// Inputs 2 double3's that represnet HSL colors, converts them to RGB, blends them, then outputs a Color.
		/// </summary>
		public static Color BlendHSL(this double3 color, double3 backColor)
		{
			Color rgb1 = ColorConvert.HSLToRGB(color.X, color.Y, color.Z).ToColor();
			Color rgb2 = ColorConvert.HSLToRGB(backColor.X, backColor.Y, backColor.Z).ToColor();

			return BlendRGB(rgb1, rgb2);

		}
	}

	static class ColorConvert
	{
		/// <summary>
		/// returns a double3 that represents an HSL color.
		/// </summary>
		public static double3 ColorToHSL(Color color)
		{
			byte r = color.R;
			byte g = color.G;
			byte b = color.B;

			return RGBToHSL(r, g, b);
		}

		//HSL Color Blending
		public static double3 RGBToHSL(byte R, byte G, byte B)
		{
			float _R = (R / 255f);
			float _G = (G / 255f);
			float _B = (B / 255f);

			float _Min = Math.Min(Math.Min(_R, _G), _B);
			float _Max = Math.Max(Math.Max(_R, _G), _B);
			float _Delta = _Max - _Min;

			float H = 0;
			float S = 0;
			float L = (float)((_Max + _Min) / 2.0f);

			if (_Delta != 0)
			{
				if (L < 0.5f)
				{
					S = (float)(_Delta / (_Max + _Min));
				}
				else
				{
					S = (float)(_Delta / (2.0f - _Max - _Min));
				}


				if (_R == _Max)
				{
					H = (_G - _B) / _Delta;
				}
				else if (_G == _Max)
				{
					H = 2f + (_B - _R) / _Delta;
				}
				else if (_B == _Max)
				{
					H = 4f + (_R - _G) / _Delta;
				}
			}

			return new double3(H, S, L);
		}

		public static double3 HSLToRGB(double h, double s, double l)
		{
			byte r, g, b;
			if (s == 0)
			{
				r = (byte)Math.Round(l * 255d);
				g = (byte)Math.Round(l * 255d);
				b = (byte)Math.Round(l * 255d);
			}
			else
			{
				double t1, t2;
				double th = h / 6.0d;

				if (l < 0.5d)
				{
					t2 = l * (1d + s);
				}
				else
				{
					t2 = (l + s) - (l * s);
				}
				t1 = 2d * l - t2;

				double tr, tg, tb;
				tr = th + (1.0d / 3.0d);
				tg = th;
				tb = th - (1.0d / 3.0d);

				tr = ColorCalc(tr, t1, t2);
				tg = ColorCalc(tg, t1, t2);
				tb = ColorCalc(tb, t1, t2);
				r = (byte)Math.Round(tr * 255d);
				g = (byte)Math.Round(tg * 255d);
				b = (byte)Math.Round(tb * 255d);
			}
			return new double3(r, g, b);
		}

		private static double ColorCalc(double c, double t1, double t2)
		{

			if (c < 0) c += 1d;
			if (c > 1) c -= 1d;
			if (6.0d * c < 1.0d) return t1 + (t2 - t1) * 6.0d * c;
			if (2.0d * c < 1.0d) return t2;
			if (3.0d * c < 2.0d) return t1 + (t2 - t1) * (2.0d / 3.0d - c) * 6.0d;
			return t1;
		}
	}
}