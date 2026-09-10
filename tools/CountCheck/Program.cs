using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using MajSimai;

// 物量口径核对工具：
// 用与 App 相同的 MajSimai 解析器解析谱面，分别按「旧移动端逻辑」与
// 「新逻辑（=渲染器 CountNoteSum / AstroDX 口径）」统计各难度物量。

internal static class Program
{
    private static async Task Main(string[] args)
    {
        var path = args.Length > 0 ? args[0] : @"D:\Workspace\build\TestChart-backup\maidata.txt";
        var text = await File.ReadAllTextAsync(path, Encoding.UTF8);
        var file = await SimaiParser.ParseAsync(text, "countcheck");

        Console.WriteLine($"title={file.Title} artist={file.Artist} charts={file.Charts.Length}");
        for (var d = 0; d < file.Charts.Length; d++)
        {
            var chart = file.Charts[d];
            if (chart.IsEmpty) continue;
            var (oldTap, oldHold, oldSlide, oldTouch, oldBrk) = CountOld(chart);
            var (newTap, newHold, newSlide, newTouch, newBrk) = CountNew(chart);
            var (breakTaps, headedSlides, noHeadSlides, breakSlideHeads, breakSlideBodies) = Breakdown(chart);
            var comp = Components(chart);
            Console.WriteLine($"--- diff index {d} (inote_{d + 1}) ---");
            Console.WriteLine($"OLD: tap={oldTap} hold={oldHold} slide={oldSlide} touch={oldTouch} break={oldBrk} total={oldTap + oldHold + oldSlide + oldTouch + oldBrk}");
            Console.WriteLine($"NEW: tap={newTap} hold={newHold} slide={newSlide} touch={newTouch} break={newBrk} total={newTap + newHold + newSlide + newTouch + newBrk}");
            Console.WriteLine($"breakdown: breakTaps={breakTaps} headedSlides={headedSlides} noHeadSlides={noHeadSlides} breakSlideHeads={breakSlideHeads} breakSlideBodies={breakSlideBodies}");
            Console.WriteLine($"components: nonBreakTaps={comp.nonBreakTaps} nonBreakHeadedSlides={comp.nonBreakHeadedSlides} isBreakHeads={comp.isBreakHeads} sbOnlyHeads={comp.sbOnlyHeads} allTaps={comp.allTaps} nonBreakNoHeadSlides={comp.nonBreakNoHeadSlides}");
        }
    }

    // 旧移动端逻辑（上一版 UpdateNoteCounts）
    private static (int tap, int hold, int slide, int touch, int brk) CountOld(SimaiChart chart)
    {
        var tap = 0; var hold = 0; var slide = 0; var touch = 0; var brk = 0;
        foreach (var tp in chart.NoteTimings)
            foreach (var n in tp.Notes)
            {
                if (n.IsBreak) brk++;
                if (n.IsSlideBreak) brk++;
                switch (n.Type)
                {
                    case SimaiNoteType.Tap: tap++; break;
                    case SimaiNoteType.Hold:
                    case SimaiNoteType.TouchHold:
                        if (!n.IsBreak && !n.IsSlideBreak) hold++;
                        break;
                    case SimaiNoteType.Slide:
                        if (!n.IsSlideNoHead) slide++;
                        break;
                    case SimaiNoteType.Touch:
                        if (!n.IsBreak && !n.IsSlideBreak) touch++;
                        break;
                }
            }
        return (tap, hold, slide, touch, brk);
    }

    // 新逻辑（= ObjectCounter.CountNoteSum）
    private static (int tap, int hold, int slide, int touch, int brk) CountNew(SimaiChart chart)
    {
        var tap = 0; var hold = 0; var slide = 0; var touch = 0; var brk = 0;
        foreach (var tp in chart.NoteTimings)
            foreach (var n in tp.Notes)
            {
                if (!n.IsBreak)
                {
                    switch (n.Type)
                    {
                        case SimaiNoteType.Tap: tap++; break;
                        case SimaiNoteType.Hold:
                        case SimaiNoteType.TouchHold: hold++; break;
                        case SimaiNoteType.Slide:
                            if (!n.IsSlideNoHead) tap++; // 星星头计入 Tap
                            if (n.IsSlideBreak) brk++; else slide++;
                            break;
                        case SimaiNoteType.Touch: touch++; break;
                    }
                }
                else
                {
                    if (n.Type == SimaiNoteType.Slide)
                    {
                        if (!n.IsSlideNoHead) brk++;
                        if (n.IsSlideBreak) brk++; else slide++;
                    }
                    else brk++;
                }
            }
        return (tap, hold, slide, touch, brk);
    }

    private static (int breakTaps, int headedSlides, int noHeadSlides, int breakSlideHeads, int breakSlideBodies) Breakdown(SimaiChart chart)
    {
        var breakTaps = 0; var headedSlides = 0; var noHeadSlides = 0;
        var breakSlideHeads = 0; var breakSlideBodies = 0;
        foreach (var tp in chart.NoteTimings)
            foreach (var n in tp.Notes)
            {
                if (n.Type == SimaiNoteType.Slide)
                {
                    if (n.IsSlideNoHead)
                    {
                        noHeadSlides++;
                        if (n.IsBreak || n.IsSlideBreak) breakSlideBodies++;
                    }
                    else
                    {
                        headedSlides++;
                        if (n.IsBreak || n.IsSlideBreak) breakSlideHeads++;
                    }
                }
                else if (n.Type == SimaiNoteType.Tap && n.IsBreak)
                {
                    breakTaps++;
                }
            }
        return (breakTaps, headedSlides, noHeadSlides, breakSlideHeads, breakSlideBodies);
    }

    private static (int nonBreakTaps, int nonBreakHeadedSlides, int isBreakHeads, int sbOnlyHeads, int allTaps, int nonBreakNoHeadSlides) Components(SimaiChart chart)
    {
        var nonBreakTaps = 0; var nonBreakHeadedSlides = 0; var isBreakHeads = 0;
        var sbOnlyHeads = 0; var allTaps = 0; var nonBreakNoHeadSlides = 0;
        foreach (var tp in chart.NoteTimings)
            foreach (var n in tp.Notes)
            {
                if (n.Type == SimaiNoteType.Tap)
                {
                    allTaps++;
                    if (!n.IsBreak) nonBreakTaps++;
                }
                else if (n.Type == SimaiNoteType.Slide)
                {
                    if (n.IsSlideNoHead)
                    {
                        if (!n.IsBreak && !n.IsSlideBreak) nonBreakNoHeadSlides++;
                    }
                    else if (n.IsBreak) isBreakHeads++;
                    else if (n.IsSlideBreak) sbOnlyHeads++;
                    else nonBreakHeadedSlides++;
                }
            }
        return (nonBreakTaps, nonBreakHeadedSlides, isBreakHeads, sbOnlyHeads, allTaps, nonBreakNoHeadSlides);
    }
}
