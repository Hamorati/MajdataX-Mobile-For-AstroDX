namespace Unity.Mathematics
{
    public struct float2
    {
        public float x;
        public float y;

        public float2(float x, float y)
        {
            this.x = x;
            this.y = y;
        }

        public static readonly float2 zero = new float2(0f, 0f);
    }

    public static class math
    {
        public const float PI = (float)System.Math.PI;

        public static float cos(float v) => (float)System.Math.Cos(v);
        public static float sin(float v) => (float)System.Math.Sin(v);
    }
}
