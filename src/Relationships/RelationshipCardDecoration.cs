using System;
namespace Dafeiyu
{
    public static class RelationshipCardDecoration
    {
        public const string Version="keepsake-ornaments-2";
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
                case "christmas":return "M 5,22 L 17,2 L 29,22 Z M 11,12 L 23,12 M 17,22 L 17,25 M 45,3 L 45,23 M 36,8 L 54,18 M 36,18 L 54,8 M 64,13 L 116,13";
                case "spring-festival":return "M 5,4 L 29,4 L 29,21 L 5,21 Z M 17,0 L 17,4 M 17,21 L 17,26 M 11,4 C 7,12 7,14 11,21 M 23,4 C 27,12 27,14 23,21 M 42,13 L 52,3 L 62,13 L 52,23 Z M 68,13 L 116,13";
                case "lantern":return "M 6,3 L 28,3 M 17,3 C 0,6 0,20 17,22 C 34,20 34,6 17,3 M 17,22 L 17,26 M 44,7 L 64,7 M 54,7 C 41,9 41,20 54,22 C 67,20 67,9 54,7 M 76,13 L 116,13";
                case "valentines-day":return "M 18,23 C -8,7 4,-6 18,5 C 32,-6 44,7 18,23 Z M 48,13 C 64,1 72,25 86,13 L 116,13";
                case "qingming":return "M 5,2 C 30,7 32,18 36,24 M 14,5 C 8,13 9,18 7,21 M 24,11 C 18,16 18,21 17,24 M 52,2 L 65,12 L 53,23 L 40,12 Z M 53,23 C 54,15 74,24 87,13 L 116,13";
                case "dragon-boat":return "M 3,14 L 38,14 L 31,23 L 13,23 Z M 8,14 L 5,7 L 12,5 L 18,14 M 21,14 L 21,2 M 21,3 L 32,10 L 21,10 M 49,22 L 62,2 L 75,22 Z M 56,11 L 69,14 M 82,13 L 116,13";
                case "mid-autumn":return "M 25,2 C 1,-3 -4,27 22,24 C 9,22 7,8 25,2 Z M 42,12 L 50,4 L 58,12 L 50,20 Z M 50,4 L 50,20 M 42,12 L 58,12 M 68,13 L 116,13";
                case "national-day":return "M 17,2 L 21,10 L 30,11 L 23,17 L 25,25 L 17,21 L 9,25 L 11,17 L 4,11 L 13,10 Z M 40,7 C 62,23 76,1 96,13 L 116,13 M 40,19 C 62,3 76,25 96,13";
                case "qixi":return "M 4,20 C 20,3 33,3 49,20 M 10,20 C 22,10 32,10 43,20 M 62,11 C 56,1 70,1 74,9 L 83,5 L 78,13 L 66,18 Z M 92,2 L 94,8 L 100,10 L 94,12 L 92,18 L 90,12 L 84,10 L 90,8 Z M 104,13 L 116,13";
                default:return "M 3,13 L 114,13";
            }
        }
    }
}
