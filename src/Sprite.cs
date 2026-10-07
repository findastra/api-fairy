using System;
using System.Collections.Generic;

namespace ApiFairy
{
    // The fairy, drawn pixel by pixel on a 32x24 LCD, the same way Data Dealer's Chip is.
    public static class Sprite
    {
        public const int W = 32, H = 24;

        // ARGB
        public const uint Bg = 0xFFCFE5CC, Ink = 0xFF1B2A1C, Mid = 0xFF6E8C6F, Light = 0xFFAECDAA;

        // '#' ink, '+' wing edge, '-' wing fill, 'o' solid background (hides the wings behind the body)
        static readonly string[] Body =
        {
            "................................",
            ".................#..............",
            "...............##...............",
            "..............####..............",
            "............##oooo##............",
            "...........#oooooooo#...........",
            "...........#oooooooo#...........",
            "...........#oooooooo#...........",
            "...........#oooooooo#...........",
            "............##oooo##............",
            "..............####..............",
            "...............##...............",
            "..............#oo#..............",
            ".............#oooo#.............",
            ".............#oooo#.............",
            "............#oooooo#............",
            "...........#oooooooo#...........",
            "...........##########...........",
            "..............#..#..............",
            ".............##..##.............",
        };

        static readonly string[] WingsUp = Wings(6, new[]
        {
            "....+++", "...+---+", "...+----+", "....+----+", ".....+----+", "......++----+",
            "........++++++", ".........+--+", "........+---+", "........+--+", ".........++",
        });

        static readonly string[] WingsDown = Wings(8, new[]
        {
            ".....+++", "....+---++", "....+-----+", ".....+-----+", "......++++++++",
            ".........+--+", "........+---+", ".......+---+", ".......+--+", "........++",
        });

        // Wings are drawn as left halves and mirrored, so both sides always match.
        static string[] Wings(int top, string[] leftHalves)
        {
            var rows = new string[H];
            for (int y = 0; y < H; y++) rows[y] = new string('.', W);
            for (int i = 0; i < leftHalves.Length; i++)
            {
                var half = leftHalves[i].PadRight(16, '.');
                var chars = half.ToCharArray();
                Array.Reverse(chars);
                rows[top + i] = half + new string(chars);
            }
            return rows;
        }

        public sealed class Frame
        {
            public string Mood = "happy";
            public bool WingsUp = true;
            public bool Blink;
            public int Bob;
            public bool Twinkle;
        }

        // Advances bob, wing flaps, blinks and twinkles once per tick, like a little pet clock.
        public sealed class Clock
        {
            readonly Frame frame = new Frame();
            readonly Random rng = new Random();
            int ticks, blinkIn = 10;

            public uint[] Next(string mood, bool reduceMotion)
            {
                ticks++;
                bool alert = mood == "alert";
                frame.Mood = mood;
                frame.Bob = reduceMotion ? 0 : (ticks / 2 % 2 == 0 ? 0 : -1);
                frame.WingsUp = reduceMotion || (alert ? ticks % 2 == 0 : ticks / 2 % 2 == 0);
                frame.Twinkle = ticks / 3 % 2 == 0;
                blinkIn--;
                frame.Blink = blinkIn <= 0;
                if (blinkIn <= -1) blinkIn = 8 + rng.Next(14);
                return Render(frame);
            }
        }

        public static uint[] Render(Frame f)
        {
            var px = new uint[W * H];
            for (int i = 0; i < px.Length; i++) px[i] = Bg;
            bool sleep = f.Mood == "sleep";
            int bob = sleep ? 0 : f.Bob;
            Layer(px, sleep || !f.WingsUp ? WingsDown : WingsUp, bob);
            Layer(px, Body, bob);

            Action<int, int, uint> p = (x, y, c) => Put(px, x, y + bob, c);
            Action<int, int, uint> fixedP = (x, y, c) => Put(px, x, y, c);
            Action openEyes = () => { p(13, 5, Ink); p(13, 6, Ink); p(18, 5, Ink); p(18, 6, Ink); };
            Action shutEyes = () => { p(12, 6, Ink); p(13, 6, Ink); p(18, 6, Ink); p(19, 6, Ink); };
            Action eyes = () => { if (f.Blink) shutEyes(); else openEyes(); };
            Action cheeks = () => { p(12, 7, Mid); p(19, 7, Mid); };

            switch (f.Mood)
            {
                case "sleep":
                    shutEyes();
                    p(15, 8, Ink); p(16, 8, Ink);
                    int zy = f.Twinkle ? 0 : 1;
                    foreach (var xy in new[] { 24, 1, 25, 1, 26, 1, 27, 1, 26, 2, 25, 3, 24, 4, 25, 4, 26, 4, 27, 4 }.Pairs()) fixedP(xy.Key, xy.Value + zy, Ink);
                    break;
                case "alert":
                    openEyes(); cheeks();
                    p(15, 7, Ink); p(16, 7, Ink); p(15, 8, Ink); p(16, 8, Ink);
                    foreach (var xy in new[] { 13, 12, 12, 11, 11, 10, 18, 12, 19, 11, 20, 10 }.Pairs()) p(xy.Key, xy.Value, Ink);
                    // a little key floating up by her hand
                    foreach (var xy in new[] { 23, 1, 24, 1, 25, 1, 23, 2, 25, 2, 26, 2, 27, 2, 28, 2, 29, 2, 23, 3, 24, 3, 25, 3, 27, 3, 29, 3 }.Pairs()) fixedP(xy.Key, xy.Value, Ink);
                    Star(fixedP, 4, 4, f.Twinkle);
                    Star(fixedP, 27, 14, !f.Twinkle);
                    break;
                case "worried":
                    eyes();
                    p(14, 8, Ink); p(15, 7, Ink); p(16, 7, Ink); p(17, 8, Ink);
                    fixedP(22, 4, Mid); fixedP(22, 5, Mid);
                    if (f.Twinkle) fixedP(22, 6, Mid);
                    break;
                case "curious":
                    eyes(); cheeks();
                    p(15, 8, Ink); p(16, 8, Ink);
                    fixedP(24, 1, Ink); fixedP(24, 2, Ink); fixedP(24, 3, Ink); fixedP(24, 5, Ink);
                    if (f.Twinkle) { fixedP(23, 1, Ink); fixedP(25, 1, Ink); }
                    break;
                default:
                    eyes(); cheeks();
                    p(14, 7, Ink); p(15, 8, Ink); p(16, 8, Ink); p(17, 7, Ink);
                    Star(fixedP, 26, 3, f.Twinkle);
                    break;
            }
            return px;
        }

        static void Star(Action<int, int, uint> put, int cx, int cy, bool big)
        {
            put(cx, cy, Ink);
            if (!big) return;
            put(cx - 1, cy, Ink); put(cx + 1, cy, Ink); put(cx, cy - 1, Ink); put(cx, cy + 1, Ink);
        }

        static IEnumerable<KeyValuePair<int, int>> Pairs(this int[] xs)
        {
            for (int i = 0; i + 1 < xs.Length; i += 2) yield return new KeyValuePair<int, int>(xs[i], xs[i + 1]);
        }

        static void Layer(uint[] px, string[] rows, int bob)
        {
            for (int y = 0; y < rows.Length; y++)
            {
                var row = rows[y];
                for (int x = 0; x < W && x < row.Length; x++)
                {
                    uint c;
                    switch (row[x])
                    {
                        case '#': c = Ink; break;
                        case '+': c = Mid; break;
                        case '-': c = Light; break;
                        case 'o': c = Bg; break;
                        default: continue;
                    }
                    Put(px, x, y + bob, c);
                }
            }
        }

        static void Put(uint[] px, int x, int y, uint c)
        {
            if (x >= 0 && x < W && y >= 0 && y < H) px[y * W + x] = c;
        }
    }
}
