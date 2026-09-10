#nullable enable

using System.Collections.Generic;
using System.Text;

namespace MajdataViewX.Mobile
{
    /// <summary>
    /// 谱面文本编辑变换（移植自原生 MajdataEdit-Neo 的 Mirror/Subdivide 插件，语义一致）：
    /// 对"选中文本"做字符级变换：左右翻转、上下翻转、180° 翻转、45° 顺时针/逆时针旋转、
    /// 乐句 1.5x / 2x 细分。忽略 {}、()、&lt;HS*…&gt; 等设置段，保留空白位置。
    /// </summary>
    public static class MobileSimaiEdit
    {
        public enum EditOp
        {
            MirrorLR = 0,     // 左右翻转选择
            MirrorUD = 1,     // 上下翻转选择
            Mirror180 = 2,    // 180° 翻转选择
            Rotate45CW = 3,   // 45° 顺时针旋转选择
            Rotate45CCW = 4,  // 45° 逆时针旋转选择
            Subdivide1p5 = 5, // 将乐句 1.5 倍细分
            Subdivide2 = 6    // 将乐句 2 倍细分
        }

        public static string Apply(string text, EditOp op)
        {
            return op switch
            {
                EditOp.Subdivide1p5 => SimaiSubdivide.Subdivide(text, 1.5f),
                EditOp.Subdivide2 => SimaiSubdivide.Subdivide(text, 2.0f),
                _ => SimaiMirror.HandleMirror(text, (SimaiMirror.HandleType)op)
            };
        }

        static class SimaiMirror
        {
            public enum HandleType
            {
                LRMirror = 0,
                UDMirror = 1,
                HalfRotation = 2,
                Rotation45 = 3,
                CcwRotation45 = 4
            }

            private static readonly Dictionary<char, char> MIRROR_LEFT_RIGHT_MAP = new()
            {
                { '1', '8' }, { '2', '7' }, { '3', '6' }, { '4', '5' },
                { '5', '4' }, { '6', '3' }, { '7', '2' }, { '8', '1' },
                { 'q', 'p' }, { 'p', 'q' },
                { '<', '>' }, { '>', '<' },
                { 'z', 's' }, { 's', 'z' }
            };

            // 当遇到这些字符时 使用特殊的映射表（D/E 区触控的特殊处理）
            private static readonly HashSet<char> MIRROR_SPECIAL_PREFIX = new() { 'D', 'E' };

            private static readonly Dictionary<char, char> MIRROR_LEFT_RIGHT_SPECIAL_MAP = new()
            {
                { '8', '2' }, { '2', '8' }, { '3', '7' }, { '7', '3' },
                { '4', '6' }, { '6', '4' }, { '1', '1' }, { '5', '5' }
            };

            private static readonly Dictionary<char, char> MIRROR_UPSIDE_DOWN_MAP = new()
            {
                { '4', '1' }, { '5', '8' }, { '6', '7' }, { '3', '2' },
                { '7', '6' }, { '2', '3' }, { '8', '5' }, { '1', '4' },
                { 'q', 'p' }, { 'p', 'q' },
                { 'z', 's' }, { 's', 'z' }
            };

            private static readonly Dictionary<char, char> MIRROR_UPSIDE_DOWN_SPECIAL_MAP = new()
            {
                { '4', '2' }, { '2', '4' }, { '1', '5' }, { '5', '1' },
                { '8', '6' }, { '6', '8' }, { '3', '3' }, { '7', '7' }
            };

            private static readonly Dictionary<char, char> ROTATE_CW_45_MAP = new()
            {
                { '8', '1' }, { '7', '8' }, { '6', '7' }, { '5', '6' },
                { '4', '5' }, { '3', '4' }, { '2', '3' }, { '1', '2' }
            };

            private static readonly Dictionary<char, char> ROTATE_CCW_45_MAP = new()
            {
                { '1', '8' }, { '2', '1' }, { '3', '2' }, { '4', '3' },
                { '5', '4' }, { '6', '5' }, { '7', '6' }, { '8', '7' }
            };

            private static readonly HashSet<char> ROTATE_CW_45_SPECIAL_PREFIX = new() { '2', '6' };
            private static readonly HashSet<char> ROTATE_CCW_45_SPECIAL_PREFIX = new() { '3', '7' };

            private static readonly Dictionary<char, char> ROTATE_45_SPECIAL_MAP = new()
            {
                { '<', '>' }, { '>', '<' }
            };

            private static readonly string HS_SEQUENCE = "<HS*";

            public static string HandleMirror(string str, HandleType type)
            {
                // NOTE: 类似 1-5[8:1]{16} 这样的字符串无法被正确镜像——原生行为，保持一致。

                var resultString = new StringBuilder();
                var curPart = new StringBuilder();
                var isPartIgnored = false;
                var hsStatus = 0;

                // 空白字符会被正常加入每一个 part，在子方法中做忽略处理，以保持空白位置不变
                foreach (var c in str)
                {
                    curPart.Append(c);

                    if (!isPartIgnored && (c == '{' || c == '}' || c == '(' || c == ')'))
                        isPartIgnored = true;

                    if (hsStatus == 0)
                    {
                        if (HS_SEQUENCE[0] == c)
                            hsStatus = 1;
                    }
                    else if (hsStatus != HS_SEQUENCE.Length)
                    {
                        if (!char.IsWhiteSpace(c))
                        {
                            if (HS_SEQUENCE[hsStatus] == c)
                            {
                                hsStatus++;
                                if (hsStatus == HS_SEQUENCE.Length)
                                    isPartIgnored = true;
                            }
                            else
                            {
                                hsStatus = 0;
                            }
                        }
                    }

                    // 以下字符表示本 part 结束
                    if (c == '}' || c == ')' || c == ',' || c == '/' || c == '`' ||
                        (hsStatus == 4 && c == '>'))
                    {
                        resultString.Append(isPartIgnored ? curPart.ToString() : NoteMirrorPart(curPart.ToString(), type));
                        isPartIgnored = false;
                        hsStatus = 0;
                        curPart.Clear();
                    }
                }

                if (curPart.Length > 0)
                {
                    resultString.Append(isPartIgnored ? curPart.ToString() : NoteMirrorPart(curPart.ToString(), type));
                }

                return resultString.ToString();
            }

            private static string NoteMirrorPart(string str, HandleType type)
            {
                switch (type)
                {
                    case HandleType.LRMirror:
                        str = NormalMirrorPart(str, MIRROR_LEFT_RIGHT_MAP, MIRROR_LEFT_RIGHT_SPECIAL_MAP, MIRROR_SPECIAL_PREFIX);
                        break;
                    case HandleType.UDMirror:
                        str = NormalMirrorPart(str, MIRROR_UPSIDE_DOWN_MAP, MIRROR_UPSIDE_DOWN_SPECIAL_MAP, MIRROR_SPECIAL_PREFIX);
                        break;
                    case HandleType.HalfRotation:
                        // 180 = 左右 + 上下
                        str = NormalMirrorPart(str, MIRROR_LEFT_RIGHT_MAP, MIRROR_LEFT_RIGHT_SPECIAL_MAP, MIRROR_SPECIAL_PREFIX);
                        str = NormalMirrorPart(str, MIRROR_UPSIDE_DOWN_MAP, MIRROR_UPSIDE_DOWN_SPECIAL_MAP, MIRROR_SPECIAL_PREFIX);
                        break;
                    case HandleType.Rotation45:
                        str = NormalMirrorPart(str, ROTATE_CW_45_MAP, ROTATE_45_SPECIAL_MAP, ROTATE_CW_45_SPECIAL_PREFIX);
                        break;
                    case HandleType.CcwRotation45:
                        str = NormalMirrorPart(str, ROTATE_CCW_45_MAP, ROTATE_45_SPECIAL_MAP, ROTATE_CCW_45_SPECIAL_PREFIX);
                        break;
                }

                return str;
            }

            private static string NormalMirrorPart(string str, Dictionary<char, char> normalMap, Dictionary<char, char> specialMap, HashSet<char> specialPrefix)
            {
                var result = new StringBuilder();
                var isSpecialPrefix = false;
                var isInBracket = false;

                foreach (var c in str)
                {
                    // 空白字符忽略
                    if (char.IsWhiteSpace(c))
                    {
                        result.Append(c);
                        continue;
                    }

                    if (isInBracket || c == '[')
                    {
                        // 方括号内是时长等设置，原样保留
                        result.Append(c);
                    }
                    else if (isSpecialPrefix)
                    {
                        isSpecialPrefix = false;
                        if (specialMap.TryGetValue(c, out var value))
                        {
                            result.Append(value);
                        }
                        else if (int.TryParse(c.ToString(), out _) && normalMap.TryGetValue(c, out var value1))
                        {
                            result.Append(value1);
                        }
                        else
                        {
                            result.Append(c);
                        }
                    }
                    else
                    {
                        result.Append(normalMap.TryGetValue(c, out var value) ? value : c);
                    }

                    if (c == '[')
                    {
                        isInBracket = true;
                    }
                    else if (c == ']')
                    {
                        isInBracket = false;
                    }
                    if (specialPrefix.Contains(c))
                    {
                        isSpecialPrefix = true;
                    }
                }

                return result.ToString();
            }
        }

        static class SimaiSubdivide
        {
            public static string Subdivide(string phrase, float multiplier)
            {
                var strs = phrase.Split('{');
                var result = "";
                foreach (var str in strs)
                {
                    result += SubdivideInternal("{" + str, multiplier);
                }
                return result;
            }

            private static string SubdivideInternal(string phrase, float multiplier)
            {
                if (multiplier <= 1) return phrase; // Invalid multiplier, return original phrase

                var startIndex = phrase.IndexOf('{') + 1;
                var endIndex = phrase.IndexOf('}');

                if (startIndex == 0 || endIndex == -1 || endIndex <= startIndex) return phrase;

                // 提取 {n} 细分号与内容
                var originalSubdivision = int.Parse(phrase.Substring(startIndex, endIndex - startIndex));
                var content = phrase.Substring(endIndex + 1);

                var newSubdivision = originalSubdivision * multiplier;
                var diff = System.Math.Abs(System.Math.Truncate(newSubdivision) - newSubdivision);
                if (!(diff < 0.0000001 || diff > 0.9999999)) return phrase; // 细分必须为整数

                var newCommaCount = CountCommas(content) * multiplier;
                var diff1 = System.Math.Abs(System.Math.Truncate(newCommaCount) - newCommaCount);
                if (!(diff1 < 0.0000001 || diff1 > 0.9999999)) return phrase; // 逗号数必须为整数

                var expandedContent = new StringBuilder();
                double fractionalComma = 0.0;
                foreach (var c in content)
                {
                    if (c == ',')
                    {
                        fractionalComma += multiplier;
                        var repeatCount = (int)fractionalComma;
                        fractionalComma -= repeatCount;
                        expandedContent.Append(new string(',', repeatCount));
                    }
                    else
                    {
                        expandedContent.Append(c);
                    }
                }

                return $"{{{newSubdivision}}}{expandedContent}";
            }

            private static int CountCommas(string s)
            {
                var n = 0;
                foreach (var c in s)
                    if (c == ',') n++;
                return n;
            }
        }
    }
}
