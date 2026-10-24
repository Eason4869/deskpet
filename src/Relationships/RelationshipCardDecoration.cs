using System;
namespace Dafeiyu
{
    public static class RelationshipCardDecoration
    {
        public const string Version="keepsake-ornaments-1";
        public const double Width=210,Height=26,ScaleX=1.72,ScaleY=.85,Inset=3;
        // 120 by 24 design coordinates, stretched to a 210 by 26 native vector strip: never combined into title glyphs or portrait circles.
        public static string PathFor(string stage)
        {
            switch(stage){
                case "first_meeting":return "M 2,5 L 30,5 L 30,21 L 2,21 Z M 2,5 L 16,15 L 30,5 M 35,13 L 112,13";
                case "familiar":return "M 3,14 L 31,2 L 23,22 L 17,15 L 3,14 M 17,15 L 31,2 M 39,17 C 55,1 62,24 81,11 C 91,4 103,7 114,10";
                case "close":return "M 5,21 C 14,16 20,9 27,3 M 13,16 C 2,16 1,6 2,3 C 14,3 18,9 13,16 Z M 20,10 C 21,1 31,0 35,2 C 35,11 29,15 20,10 Z M 47,21 C 56,16 62,9 69,3 M 55,16 C 44,16 43,6 44,3 C 56,3 60,9 55,16 Z M 62,10 C 63,1 73,0 77,2 C 77,11 71,15 62,10 Z M 85,13 L 116,13";
                case "trusted":return "M 2,2 L 14,2 L 14,23 L 8,18 L 2,23 Z M 25,21 C 49,20 78,9 116,6 M 42,18 C 30,17 29,8 31,5 C 44,5 48,11 42,18 Z M 58,14 C 55,3 64,0 72,2 C 72,12 67,17 58,14 Z M 75,10 C 78,21 90,22 98,17 C 95,8 86,6 75,10 Z";
                case "heartfelt":return "M 3,5 L 32,5 L 32,21 L 3,21 Z M 3,5 L 17,15 L 32,5 M 15,5 C 7,-1 18,-3 18,3 C 20,-3 30,-1 23,5 M 40,13 L 114,13";
                case "best_friend":return "M 24,10 C 4,-6 -8,9 14,15 C 20,16 23,13 24,10 C 40,-6 60,5 41,14 C 34,18 27,14 24,10 M 20,13 L 10,25 L 20,20 L 24,24 L 28,13 M 29,14 L 42,25 L 36,18 L 45,20 L 35,12 M 50,12 C 69,13 81,12 95,12 L 116,12";
                case "halloween":return "M 16,7 C 0,-2 -3,22 12,23 C 23,27 31,20 29,10 C 27,3 21,2 16,7 M 15,7 C 17,0 21,0 24,2 M 12,8 C 6,15 9,22 12,23 M 19,7 C 24,15 23,21 20,24 M 36,14 C 52,-1 64,25 80,12 C 92,0 105,8 116,11 M 63,13 L 66,4 L 73,10 L 80,4 L 81,13";
                default:return "M 3,13 L 114,13";
            }
        }
    }
}
