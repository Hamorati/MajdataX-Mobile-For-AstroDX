using MajdataViewX.Base;
using MajdataViewX.Types.Input;
using MajdataViewX.Types.Notes;
using Unity.Burst;
using Unity.Mathematics;
using static MajdataViewX.Managers.SkinManager;

namespace MajdataViewX.Notes.NoteDatas
{
    [BurstCompile]
    public struct TouchData
    {
        public float time;
        public SensorType sensor;
        public float hspeed;
        public readonly float speed => NoteHelper.Settings.TouchSpeed * hspeed;
        public int sensorOrderIndex;

        public bool isHanabi;
        public bool isEach;
        public bool isEx;
        public bool isBreak;
        public bool isMine;
        public bool usingSV;

        /// <summary>是否为传感区锚定 slide 的启动拍 Touch（AstroDX 的 touch_star 特殊皮肤）。</summary>
        public bool isSlideHead;

        public bool isFolded;

        public bool isAppeared;
        public bool isEnd;
        public float2 centerPos;

        public float wholeDuration;
        public float moveDuration;
        public float displayDuration;

        public float fanAlpha;

        public NoteSp fanSprite;
        public NoteSp pointSprite;
        public NoteSp justSprite;
        public NoteSp headStarSprite;

        public bool isJudged;
        public JudgeGrade judgeGrade;
        public float diff;

        public int groupId;

        public void Init()
        {
            groupId = -1;
            fanAlpha = 0;

            centerPos = MajPos.GetAreaPos(sensor);

            wholeDuration = 3.209385682f * math.pow(math.abs(speed), -0.9549621752f);
            displayDuration = 0.2f * wholeDuration;
            moveDuration = 0.8f * wholeDuration;

            fanSprite = NoteSp.TOUCH;
            pointSprite = NoteSp.TOUCH_POINT;
            justSprite = NoteSp.TOUCH_JUST;

            if (isEach)
            {
                fanSprite = NoteSp.TOUCH_EACH;
                pointSprite = NoteSp.TOUCH_POINT_EACH;
            }
            if (isBreak)
            {
                fanSprite = NoteSp.TOUCH_BREAK;
                pointSprite = NoteSp.TOUCH_POINT_BREAK;
            }
            if (isMine)
            {
                if (isBreak)
                {
                    fanSprite = NoteSp.TOUCH_BREAK_MINE;
                    pointSprite = NoteSp.TOUCH_POINT_BREAK_MINE;
                }
                else
                {
                    fanSprite = NoteSp.TOUCH_MINE;
                    pointSprite = NoteSp.TOUCH_POINT_MINE;
                }
            }

            // 传感区锚定 slide 的启动拍 Touch：pad 内显示星形（复用星形音符皮肤，适配 MajdataX 美术风格）
            if (isSlideHead)
            {
                if (isMine && isBreak)
                    headStarSprite = NoteSp.STAR_BREAK_MINE;
                else if (isMine)
                    headStarSprite = NoteSp.STAR_MINE;
                else if (isBreak)
                    headStarSprite = NoteSp.STAR_BREAK;
                else if (isEach)
                    headStarSprite = NoteSp.STAR_EACH;
                else
                    headStarSprite = NoteSp.STAR;
            }
            else
            {
                headStarSprite = default;
            }
        }

        public void Reset()
        {
            isAppeared = default;
            isEnd = default;

            fanAlpha = default;
            isJudged = default;
            judgeGrade = default;
            diff = default;
        }

        public readonly bool IsFoldable(TouchData other) =>
            time == other.time &&
            sensor == other.sensor &&
            speed == other.speed &&

            isHanabi == other.isHanabi &&
            isEach == other.isEach &&
            isEx == other.isEx &&
            isBreak == other.isBreak &&
            isMine == other.isMine &&
            isSlideHead == other.isSlideHead &&
            usingSV == other.usingSV;
    }
}
