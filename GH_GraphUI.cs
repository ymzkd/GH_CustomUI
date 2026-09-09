using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace GH_CustomUI
{
    public class GraphPlotData
    {
        public int count = 0;
        public double[] x;
        public double[] y;
        public GraphPlotData(double[] x, double[] y)
        {
            this.x = x;
            this.y = y;
        }

        public GraphPlotData() { }

        public static double[] LinspaceWithStep(double start, double step, int count)
        {
            if (count <= 0)
                return Array.Empty<double>();

            double[] result = new double[count];
            for (int i = 0; i < count; i++)
            {
                result[i] = start + step * i;
            }

            return result;
        }

        public static double[] Linspace(double start, double stop, int count)
        {
            if (count <= 0)
                return Array.Empty<double>();
            if (count == 1)
                return new double[] { start };

            double[] result = new double[count];
            double step = (stop - start) / (count - 1);

            for (int i = 0; i < count; i++)
            {
                result[i] = start + step * i;
            }

            return result;
        }
    }

    public enum GraphMarkerType
    {
        Circle,
        Square,
        Triangle,
        Diamond,
        Cross,
        Plus
    }

    public enum GraphSeekBarMode
    {
        Hidden,        // 非表示
        DisplayOnly,   // 表示のみ（操作不可）
        Interactive    // 表示+操作可能
    }

    public class GraphPlotStyle
    {
        public bool ShowLine { get; set; } = true;
        public Color LineColor { get; set; } = Color.Blue;
        public float LineWidth { get; set; } = 2.0f;
        public DashStyle LineStyle { get; set; } = DashStyle.Solid;

        public bool ShowMarkers { get; set; } = false;
        public GraphMarkerType MarkerType { get; set; } = GraphMarkerType.Circle;
        public float MarkerSize { get; set; } = 4.0f;
        public Color MarkerColor { get; set; } = Color.Red;

        public string Name { get; set; } = "Series";

        public GraphPlotStyle()
        {
        }

        public GraphPlotStyle(Color lineColor, float lineWidth = 2.0f, DashStyle lineStyle = DashStyle.Solid)
        {
            LineColor = lineColor;
            LineWidth = lineWidth;
            LineStyle = lineStyle;
        }

        /// <summary>
        /// 散布図スタイル（マーカーのみ、線なし）を作成
        /// </summary>
        public static GraphPlotStyle CreateScatterStyle(Color markerColor, GraphMarkerType markerType = GraphMarkerType.Circle, float markerSize = 4.0f)
        {
            return new GraphPlotStyle
            {
                ShowLine = false,
                ShowMarkers = true,
                MarkerColor = markerColor,
                MarkerType = markerType,
                MarkerSize = markerSize
            };
        }

        /// <summary>
        /// 線のみスタイル（マーカーなし）を作成
        /// </summary>
        public static GraphPlotStyle CreateLineStyle(Color lineColor, float lineWidth = 2.0f, DashStyle lineStyle = DashStyle.Solid)
        {
            return new GraphPlotStyle(lineColor, lineWidth, lineStyle)
            {
                ShowMarkers = false
            };
        }
    }

    public class GraphPlotSeries
    {
        public GraphPlotData Data { get; set; }
        public GraphPlotStyle Style { get; set; }
        public bool IsVisible { get; set; } = true;

        public GraphPlotSeries(GraphPlotData data, GraphPlotStyle style = null)
        {
            Data = data ?? throw new ArgumentNullException(nameof(data));
            Style = style ?? new GraphPlotStyle();
        }
    }

    /// <summary>
    /// ComponentUIパーツの基本クラス
    /// </summary>
    public class GraphUI : GH_UIParts
    {
        public List<GraphPlotSeries> Series { get; set; } = new List<GraphPlotSeries>();

        public GraphSeekBarMode SeekBarMode { get; set; } = GraphSeekBarMode.Hidden;
        public float SeekParam { get; set; } = 0.5f; // Seek position for the graph
        public float SeekPositionX => SeekParam * PlotBound.Width + PlotBound.Left; // Seek position in pixels
        private bool grab_handle = false; // Flag to indicate if the handle is being dragged
        private bool mouse_over_seek = false; // シーク領域にカーソルがあるか(離脱時にカーソルを戻すため)

        /// <summary>
        /// シークバーの位置が動くたびに呼ばれる(ドラッグ中も毎回)。
        /// 時刻ラベルなど、追従表示させたいUIの更新に使う。
        /// </summary>
        public Action<float> SeekParamChanged;

        /// <summary>
        /// シークバーの操作が確定した(マウスを離した)ときに呼ばれる。
        /// 重い処理はドラッグ中ではなくこちらで行うこと。
        /// </summary>
        public Action<float> SeekCommitted;

        public RectangleF PlotBound
        {
            get; private set;
        }

        public int XTicks { get; set; } = 6;
        public int YTicks { get; set; } = 6;

        float xMin = 0, xMax = 0;
        float yMin = 0, yMax = 0;

        public string XLabelFormat { get; set; } = "0.##"; // Format for x-axis labels
        public string YLabelFormat { get; set; } = "0.##"; // Format for y-axis labels

        public Font TickFont { get; set; } = new Font("Arial", 10, FontStyle.Regular, GraphicsUnit.Pixel);

        public float DefaultHeight { get; set; } = 150f;
        public float DefaultMinWidth { get; set; } = 150f;

        private List<string> cachedXLabels = new List<string>();
        private List<string> cachedYLabels = new List<string>();

        public string XAxisLabel { get; set; } = "";
        public string YAxisLabel { get; set; } = "";

        public float PlotMarginHorizontal { get; set; } = 5f;
        public float PlotMarginVertical { get; set; } = 5f;
        public float AxisLabelSpacing { get; set; } = 5f;

        private Font axisLabelFont = new Font("Arial", 12, FontStyle.Bold, GraphicsUnit.Pixel);
        public Font AxisLabelFont
        {
            get => axisLabelFont;
            set => axisLabelFont = value ?? new Font("Arial", 12, FontStyle.Bold, GraphicsUnit.Pixel);
        }

        public enum GraphAxisTickMode
        {
            DataRange,        // データ範囲等分割
            NiceNumbers,      // 軸範囲拡張+キリの良い目盛り
            NiceTicksOnly     // データ範囲保持+キリの良い目盛り
        }

        public GraphAxisTickMode XTickMode { get; set; } = GraphAxisTickMode.DataRange;
        public GraphAxisTickMode YTickMode { get; set; } = GraphAxisTickMode.DataRange;



        private IEnumerable<GraphPlotSeries> GetEffectiveSeries()
        {
            return Series.Where(s => s.IsVisible);
        }

        /// <summary>
        /// UIの高さを計算するメソッド
        /// </summary>
        public override float Height()
        {
            return DefaultHeight;
        }

        /// <summary>
        /// UIの描画に必要な最小幅を計算するメソッド
        /// </summary>
        /// <returns>最小幅</returns>
        public override float MinWidth()
        {
            return DefaultMinWidth;
        }

        public override void UpdateLayout()
        {
            base.UpdateLayout();

            // 軸範囲の計算（Renderから移行）
            CalculateAxisRanges();

            // 軸ラベルの計算（キャッシュ）
            cachedXLabels = CalculateXLabels();
            cachedYLabels = CalculateYLabels();

            float ylabel_width = MaxTextWidth(cachedYLabels, TickFont);
            float label_height = TickFont.Height;
            float xlast_label_width = GH_FontServer.StringWidth(cachedXLabels.Last(), TickFont);

            // 軸ラベル用スペースの計算
            float xAxisLabelHeight = !string.IsNullOrEmpty(XAxisLabel) ? axisLabelFont.Height + 5 : 0;
            float yAxisLabelWidth = !string.IsNullOrEmpty(YAxisLabel) ? axisLabelFont.Height + 5 : 0;

            RectangleF rect = Bounds;
            rect.Inflate(-PlotMarginHorizontal, -PlotMarginVertical);
            // x方向ラベル幅分オフセット（Y軸ラベル用スペースも追加）
            rect.X += ylabel_width + yAxisLabelWidth; rect.Width -= (ylabel_width + xlast_label_width * 0.5f + yAxisLabelWidth);
            // Top - yラベル高/2, Bottom - xラベル高（X軸ラベル用スペースも追加）
            rect.Y += label_height * 0.5f; rect.Height -= label_height * 1.5f + xAxisLabelHeight;

            PlotBound = rect; // Update the plot area bounds
        }

        private void CalculateAxisRanges()
        {
            var effectiveSeries = GetEffectiveSeries().ToArray();

            if (effectiveSeries.Any())
            {
                // 全シリーズから軸の範囲を計算
                var allX = effectiveSeries.SelectMany(s => s.Data.x).ToArray();
                var allY = effectiveSeries.SelectMany(s => s.Data.y).ToArray();

                if (allX.Length > 0 && allY.Length > 0)
                {
                    xMin = (float)allX.Min();
                    xMax = (float)allX.Max();
                    yMin = (float)allY.Min();
                    yMax = (float)allY.Max();

                }
                else
                {
                    // デフォルト軸範囲（データがない場合）
                    xMin = 0; xMax = 1;
                    yMin = 0; yMax = 1;
                }
            }
            else
            {
                // デフォルト軸範囲（データがない場合）
                xMin = 0; xMax = 1;
                yMin = 0; yMax = 1;
            }

            // NiceNumbers モードの場合、軸範囲をキリの良い値に拡張
            // NiceTicksOnly モードの場合、軸範囲はデータ範囲のまま（拡張しない）
            if (XTickMode == GraphAxisTickMode.NiceNumbers)
            {
                double xRange = xMax - xMin;
                if (xRange > 0)
                {
                    double xInterval = CalculateNiceTickInterval(xRange);
                    var (niceXMin, niceXMax) = CalculateNiceTickBounds(xMin, xMax, xInterval);
                    xMin = (float)niceXMin;
                    xMax = (float)niceXMax;
                }
            }

            if (YTickMode == GraphAxisTickMode.NiceNumbers)
            {
                double yRange = yMax - yMin;
                if (yRange > 0)
                {
                    double yInterval = CalculateNiceTickInterval(yRange);
                    var (niceYMin, niceYMax) = CalculateNiceTickBounds(yMin, yMax, yInterval);
                    yMin = (float)niceYMin;
                    yMax = (float)niceYMax;
                }
            }

        }

        private List<string> CalculateXLabels()
        {
            List<string> labels = new List<string>();

            if (XTickMode == GraphAxisTickMode.NiceNumbers)
            {
                // キリの良い目盛りモード（軸範囲拡張）
                double xRange = xMax - xMin;
                if (xRange > 0)
                {
                    double interval = CalculateNiceTickInterval(xRange);
                    double currentValue = xMin;

                    while (currentValue <= xMax + interval * 0.001) // 浮動小数点誤差を考慮
                    {
                        labels.Add(((float)currentValue).ToString(XLabelFormat));
                        currentValue += interval;
                    }
                }
                else
                {
                    labels.Add(xMin.ToString(XLabelFormat));
                }
            }
            else if (XTickMode == GraphAxisTickMode.NiceTicksOnly)
            {
                // キリの良い目盛りモード（データ範囲保持）
                double xRange = xMax - xMin;
                if (xRange > 0)
                {
                    double interval = CalculateNiceTickInterval(xRange);
                    var ticks = CalculateNiceTicksInRange(xMin, xMax, interval);


                    foreach (double tick in ticks)
                    {
                        labels.Add(((float)tick).ToString(XLabelFormat));
                    }
                }
                else
                {
                    labels.Add(xMin.ToString(XLabelFormat));
                }
            }
            else
            {
                // データ範囲等分割モード（従来の方式）
                float step = (xMax - xMin) / XTicks;
                for (int i = 0; i <= XTicks; i++)
                {
                    float value = xMin + i * step;
                    labels.Add(value.ToString(XLabelFormat));
                }
            }

            return labels;
        }

        private List<string> CalculateYLabels()
        {
            List<string> labels = new List<string>();

            if (YTickMode == GraphAxisTickMode.NiceNumbers)
            {
                // キリの良い目盛りモード（軸範囲拡張）
                double yRange = yMax - yMin;
                if (yRange > 0)
                {
                    double interval = CalculateNiceTickInterval(yRange);
                    double currentValue = yMin;

                    while (currentValue <= yMax + interval * 0.001) // 浮動小数点誤差を考慮
                    {
                        labels.Add(((float)currentValue).ToString(YLabelFormat));
                        currentValue += interval;
                    }
                }
                else
                {
                    labels.Add(yMin.ToString(YLabelFormat));
                }
            }
            else if (YTickMode == GraphAxisTickMode.NiceTicksOnly)
            {
                // キリの良い目盛りモード（データ範囲保持）
                double yRange = yMax - yMin;
                if (yRange > 0)
                {
                    double interval = CalculateNiceTickInterval(yRange);
                    var ticks = CalculateNiceTicksInRange(yMin, yMax, interval);

                    foreach (double tick in ticks)
                    {
                        labels.Add(((float)tick).ToString(YLabelFormat));
                    }
                }
                else
                {
                    labels.Add(yMin.ToString(YLabelFormat));
                }
            }
            else
            {
                // データ範囲等分割モード（従来の方式）
                float step = (yMax - yMin) / YTicks;
                for (int i = 0; i <= YTicks; i++)
                {
                    float value = yMin + i * step;
                    labels.Add(value.ToString(YLabelFormat));
                }
            }

            return labels;
        }

        /// <summary>
        /// データ範囲に基づいてキリの良い目盛り間隔を計算
        /// </summary>
        private double CalculateNiceTickInterval(double range)
        {
            if (range <= 0) return 1.0;

            // 大まかな目盛り間隔を計算（目標は4-8個の目盛り）
            double roughInterval = range / 6.0;

            // 10の累乗を求める
            double magnitude = Math.Pow(10, Math.Floor(Math.Log10(roughInterval)));

            // 正規化された間隔（1-10の範囲）
            double normalizedInterval = roughInterval / magnitude;

            // キリの良い値に調整
            double niceInterval;
            if (normalizedInterval <= 1.0)
                niceInterval = 1.0;
            else if (normalizedInterval <= 2.0)
                niceInterval = 2.0;
            else if (normalizedInterval <= 5.0)
                niceInterval = 5.0;
            else
                niceInterval = 10.0;

            double result = niceInterval * magnitude;

            return result;
        }

        /// <summary>
        /// キリの良い目盛り間隔に基づいて軸範囲を計算
        /// </summary>
        private (double min, double max) CalculateNiceTickBounds(double dataMin, double dataMax, double interval)
        {
            if (interval <= 0) return (dataMin, dataMax);

            // キリの良い最小値（データ最小値以下の最大の間隔倍数）
            double niceMin = Math.Floor(dataMin / interval) * interval;

            // キリの良い最大値（データ最大値以上の最小の間隔倍数）
            double niceMax = Math.Ceiling(dataMax / interval) * interval;

            return (niceMin, niceMax);
        }

        /// <summary>
        /// データ範囲内でキリの良い目盛りを生成
        /// </summary>
        private List<double> CalculateNiceTicksInRange(double dataMin, double dataMax, double interval)
        {
            List<double> ticks = new List<double>();

            if (interval <= 0 || dataMax <= dataMin)
                return ticks;

            // データ範囲を少し広げて、境界近くの目盛りも含めるようにする
            double rangeExtension = interval * 0.1; // 間隔の10%だけ範囲を拡張
            double extendedMin = dataMin - rangeExtension;
            double extendedMax = dataMax + rangeExtension;

            // 拡張範囲で最初のキリの良い値（拡張最小値以上の最小の間隔倍数）
            double firstTick = Math.Ceiling(extendedMin / interval) * interval;

            // 拡張範囲で最後のキリの良い値（拡張最大値以下の最大の間隔倍数）
            double lastTick = Math.Floor(extendedMax / interval) * interval;


            // 範囲内の全てのキリの良い目盛りを生成
            double currentTick = firstTick;
            while (currentTick <= lastTick + interval * 0.001) // 浮動小数点誤差を考慮
            {
                // 元のデータ範囲内にある目盛りのみを追加
                if (currentTick >= dataMin - interval * 0.001 && currentTick <= dataMax + interval * 0.001)
                {
                    ticks.Add(currentTick);
                }
                currentTick += interval;
            }

            return ticks;
        }

        public override void Render(GH_Canvas canvas, Graphics graphics, GH_CanvasChannel channel)
        {
            //base.Render(canvas, graphics, channel);

            if (channel == GH_CanvasChannel.Objects)
            {
                RectangleF rect = PlotBound;

                // ------------------ 領域背景表示 ---------------------
                graphics.FillRectangle(new SolidBrush(Color.Gray), rect);


                // ------------------ プロット表示 ---------------------
                var effectiveSeries = GetEffectiveSeries().ToArray();

                // 各シリーズを描画
                foreach (var series in effectiveSeries)
                {
                    RenderSeries(graphics, rect, series);
                }

                // ------------------ 軸表示 ---------------------
                float xScale = rect.Width / (xMax - xMin);
                float yScale = rect.Height / (yMax - yMin);

                Pen axisPen = new Pen(Color.Black, 1);
                Font tickFont = this.TickFont;
                Brush tickBrush = Brushes.Black;
                float tickSize = 5f;

                // 軸線
                graphics.DrawLine(axisPen, rect.Left, rect.Bottom, rect.Right, rect.Bottom); // x軸
                graphics.DrawLine(axisPen, rect.Left, rect.Top, rect.Left, rect.Bottom);     // y軸

                // x軸目盛り
                if (XTickMode == GraphAxisTickMode.NiceNumbers || XTickMode == GraphAxisTickMode.NiceTicksOnly)
                {
                    // キリの良い目盛り描画（キャッシュされたラベルに対応）
                    for (int i = 0; i < cachedXLabels.Count; i++)
                    {
                        string label = cachedXLabels[i];
                        if (float.TryParse(label, out float xVal))
                        {
                            float xPix = (xVal - xMin) * xScale + rect.Left;

                            // 目盛線
                            graphics.DrawLine(axisPen, xPix, rect.Bottom, xPix, rect.Bottom - tickSize);

                            // ラベル
                            SizeF labelSize = graphics.MeasureString(label, tickFont);
                            graphics.DrawString(label, tickFont, tickBrush,
                                xPix - labelSize.Width / 2,
                                rect.Bottom + 2);
                        }
                    }
                }
                else
                {
                    // 従来の等分割目盛り描画
                    for (int i = 0; i <= XTicks; i++)
                    {
                        float xVal = xMin + i * (xMax - xMin) / XTicks;
                        float xPix = (xVal - xMin) * xScale + rect.Left;

                        // 目盛線
                        graphics.DrawLine(axisPen, xPix, rect.Bottom, xPix, rect.Bottom - tickSize);

                        // ラベル
                        string label = i < cachedXLabels.Count ? cachedXLabels[i] : "";
                        SizeF labelSize = graphics.MeasureString(label, tickFont);
                        graphics.DrawString(label, tickFont, tickBrush,
                            xPix - labelSize.Width / 2,
                            rect.Bottom + 2);
                    }
                }

                // y軸目盛り
                if (YTickMode == GraphAxisTickMode.NiceNumbers || YTickMode == GraphAxisTickMode.NiceTicksOnly)
                {
                    // キリの良い目盛り描画（キャッシュされたラベルに対応）
                    for (int i = 0; i < cachedYLabels.Count; i++)
                    {
                        string label = cachedYLabels[i];
                        if (float.TryParse(label, out float yVal))
                        {
                            float yPix = rect.Bottom - (yVal - yMin) * yScale;

                            // 目盛線
                            graphics.DrawLine(axisPen, rect.Left, yPix, rect.Left + tickSize, yPix);

                            // ラベル
                            SizeF labelSize = graphics.MeasureString(label, tickFont);
                            graphics.DrawString(label, tickFont, tickBrush,
                                rect.Left - labelSize.Width - 2,
                                yPix - labelSize.Height / 2);
                        }
                    }
                }
                else
                {
                    // 従来の等分割目盛り描画
                    for (int i = 0; i <= YTicks; i++)
                    {
                        float yVal = yMin + i * (yMax - yMin) / YTicks;
                        float yPix = rect.Bottom - (yVal - yMin) * yScale;

                        // 目盛線
                        graphics.DrawLine(axisPen, rect.Left, yPix, rect.Left + tickSize, yPix);

                        // ラベル
                        string label = i < cachedYLabels.Count ? cachedYLabels[i] : "";
                        SizeF labelSize = graphics.MeasureString(label, tickFont);
                        graphics.DrawString(label, tickFont, tickBrush,
                            rect.Left - labelSize.Width - 2,
                            yPix - labelSize.Height / 2);
                    }
                }

                // ------------------ 軸ラベル表示 ---------------------
                // X軸ラベル（グラフ下部中央に横書き）
                if (!string.IsNullOrEmpty(XAxisLabel))
                {
                    SizeF xLabelSize = graphics.MeasureString(XAxisLabel, AxisLabelFont);
                    float xLabelX = rect.Left + (rect.Width - xLabelSize.Width) / 2;
                    float xLabelY = rect.Bottom + TickFont.Height + 5; // 目盛りラベルの下
                    graphics.DrawString(XAxisLabel, AxisLabelFont, tickBrush, xLabelX, xLabelY);
                }

                // Y軸ラベル（グラフ左端中央に縦書き）
                if (!string.IsNullOrEmpty(YAxisLabel))
                {
                    SizeF yLabelSize = graphics.MeasureString(YAxisLabel, AxisLabelFont);
                    float yLabelX = rect.Left - MaxTextWidth(cachedYLabels, TickFont) - yLabelSize.Height - AxisLabelSpacing;
                    float yLabelY = rect.Top + (rect.Height + yLabelSize.Width) / 2;

                    // テキストを90度回転して描画
                    GraphicsState state = graphics.Save();
                    graphics.TranslateTransform(yLabelX, yLabelY);
                    graphics.RotateTransform(-90);
                    graphics.DrawString(YAxisLabel, AxisLabelFont, tickBrush, 0, 0);
                    graphics.Restore(state);
                }

                // ------------------ Seek Bar表示 ---------------------
                if (SeekBarMode != GraphSeekBarMode.Hidden)
                {
                    float seekX = rect.Left + SeekParam * rect.Width;
                    graphics.DrawLine(Pens.Red, seekX, rect.Top, seekX, rect.Bottom); // Seek Bar Line
                }


                // ------------------ 領域枠表示 ---------------------
                Pen flame_pen = new Pen(Color.Black, 2);
                graphics.DrawRectangle(flame_pen, new Rectangle((int)rect.X, (int)rect.Y, (int)rect.Width, (int)rect.Height));

            }

        }


        public static GraphicsPath CreatePolylineGraphPath(RectangleF rect, float[] xData, float[] yData)
        {
            if (xData.Length != yData.Length || xData.Length == 0)
                throw new ArgumentException("xData and yData must be of equal, non-zero length.");

            // データ範囲を取得
            float xMin = Min(xData);
            float xMax = Max(xData);
            float yMin = Min(yData);
            float yMax = Max(yData);

            // データ → ピクセルへの変換
            float xScale = rect.Width / (xMax - xMin);
            float yScale = rect.Height / (yMax - yMin);

            PointF[] points = new PointF[xData.Length];
            for (int i = 0; i < xData.Length; i++)
            {
                float x = (xData[i] - xMin) * xScale + rect.Left;
                float y = rect.Bottom - (yData[i] - yMin) * yScale; // yは下が大きい座標系のため反転
                points[i] = new PointF(x, y);
            }

            // 折れ線パスを作成
            GraphicsPath path = new GraphicsPath();
            path.AddLines(points);
            return path;
        }

        private static float Min(float[] data) => data.Length == 0 ? 0 : data[Array.IndexOf(data, data.Min())];
        private static float Max(float[] data) => data.Length == 0 ? 0 : data[Array.IndexOf(data, data.Max())];

        private void RenderSeries(Graphics graphics, RectangleF rect, GraphPlotSeries series)
        {

            GraphicsPath canvasPath = new GraphicsPath();
            canvasPath.AddRectangle(rect);
            graphics.SetClip(canvasPath);

            if (series?.Data?.x == null || series.Data.y == null)
                return;

            float[] x = series.Data.x.Select(xx => (float)xx).ToArray();
            float[] y = series.Data.y.Select(yy => (float)yy).ToArray();

            if (x.Length == 0 || y.Length == 0 || x.Length != y.Length)
                return;

            // 線描画（ShowLineがtrueの場合のみ）
            if (series.Style.ShowLine)
            {
                var path = CreatePolylineGraphPathWithBounds(rect, x, y, xMin, xMax, yMin, yMax);
                using (var pen = new Pen(series.Style.LineColor, series.Style.LineWidth))
                {
                    pen.DashStyle = series.Style.LineStyle;
                    pen.LineJoin = LineJoin.Round;
                    graphics.DrawPath(pen, path);
                }
            }

            // マーカー描画
            if (series.Style.ShowMarkers)
            {
                RenderMarkers(graphics, rect, x, y, series.Style);
            }

            graphics.ResetClip();
        }

        private GraphicsPath CreatePolylineGraphPathWithBounds(RectangleF rect, float[] xData, float[] yData,
            float xMin, float xMax, float yMin, float yMax)
        {
            if (xData.Length != yData.Length || xData.Length == 0)
                throw new ArgumentException("xData and yData must be of equal, non-zero length.");

            float xScale = rect.Width / (xMax - xMin);
            float yScale = rect.Height / (yMax - yMin);

            PointF[] points = new PointF[xData.Length];
            for (int i = 0; i < xData.Length; i++)
            {
                float x = (xData[i] - xMin) * xScale + rect.Left;
                float y = rect.Bottom - (yData[i] - yMin) * yScale;
                points[i] = new PointF(x, y);
            }

            GraphicsPath path = new GraphicsPath();
            path.AddLines(points);
            return path;
        }

        private void RenderMarkers(Graphics graphics, RectangleF rect, float[] xData, float[] yData, GraphPlotStyle style)
        {
            float xScale = rect.Width / (xMax - xMin);
            float yScale = rect.Height / (yMax - yMin);

            using (var brush = new SolidBrush(style.MarkerColor))
            {
                for (int i = 0; i < xData.Length; i++)
                {
                    float x = (xData[i] - xMin) * xScale + rect.Left;
                    float y = rect.Bottom - (yData[i] - yMin) * yScale;

                    DrawMarker(graphics, new PointF(x, y), style.MarkerType, style.MarkerSize, brush);
                }
            }
        }

        private void DrawMarker(Graphics graphics, PointF center, GraphMarkerType markerType, float size, Brush brush)
        {
            float halfSize = size * 0.5f;
            RectangleF bounds = new RectangleF(center.X - halfSize, center.Y - halfSize, size, size);

            switch (markerType)
            {
                case GraphMarkerType.Circle:
                    graphics.FillEllipse(brush, bounds);
                    break;
                case GraphMarkerType.Square:
                    graphics.FillRectangle(brush, bounds);
                    break;
                case GraphMarkerType.Triangle:
                    DrawTriangle(graphics, center, size, brush);
                    break;
                case GraphMarkerType.Diamond:
                    DrawDiamond(graphics, center, size, brush);
                    break;
                case GraphMarkerType.Cross:
                    DrawCross(graphics, center, size, brush);
                    break;
                case GraphMarkerType.Plus:
                    DrawPlus(graphics, center, size, brush);
                    break;
            }
        }

        private void DrawTriangle(Graphics graphics, PointF center, float size, Brush brush)
        {
            float halfSize = size * 0.5f;
            PointF[] points = {
                new PointF(center.X, center.Y - halfSize),
                new PointF(center.X - halfSize, center.Y + halfSize),
                new PointF(center.X + halfSize, center.Y + halfSize)
            };
            graphics.FillPolygon(brush, points);
        }

        private void DrawDiamond(Graphics graphics, PointF center, float size, Brush brush)
        {
            float halfSize = size * 0.5f;
            PointF[] points = {
                new PointF(center.X, center.Y - halfSize),
                new PointF(center.X + halfSize, center.Y),
                new PointF(center.X, center.Y + halfSize),
                new PointF(center.X - halfSize, center.Y)
            };
            graphics.FillPolygon(brush, points);
        }

        private void DrawCross(Graphics graphics, PointF center, float size, Brush brush)
        {
            using (var pen = new Pen(brush, 2f))
            {
                float halfSize = size * 0.5f;
                graphics.DrawLine(pen, center.X - halfSize, center.Y - halfSize, center.X + halfSize, center.Y + halfSize);
                graphics.DrawLine(pen, center.X - halfSize, center.Y + halfSize, center.X + halfSize, center.Y - halfSize);
            }
        }

        private void DrawPlus(Graphics graphics, PointF center, float size, Brush brush)
        {
            using (var pen = new Pen(brush, 2f))
            {
                float halfSize = size * 0.5f;
                graphics.DrawLine(pen, center.X - halfSize, center.Y, center.X + halfSize, center.Y);
                graphics.DrawLine(pen, center.X, center.Y - halfSize, center.X, center.Y + halfSize);
            }
        }


        //public virtual void UpdateLayout() { }

        /// <summary>
        /// 仮にこれだけ実装。他のUIイベントも実装する。
        /// </summary>
        /// <summary>
        /// シーク操作の当たり判定。RectangleF.Contains は右端・下端を含まないため、
        /// 端(0%/100%)を確実に掴めるよう左右にわずかな余白を持たせて判定する。
        /// </summary>
        public bool IsSeekArea(PointF pt)
        {
            if (SeekBarMode != GraphSeekBarMode.Interactive)
                return false;

            const float margin = 3f;
            RectangleF rect = PlotBound;
            return pt.X >= rect.Left - margin && pt.X <= rect.Right + margin
                && pt.Y >= rect.Top && pt.Y <= rect.Bottom;
        }

        /// <summary>シークバーの線を直接掴んだとみなす、線からの距離[px]</summary>
        private const float SeekHandleGrabWidth = 5f;

        /// <summary>キャンバス座標のX位置をシークパラメータ(0-1)に変換して設定する</summary>
        private void SetSeekParamFromX(float canvasX)
        {
            RectangleF rect = PlotBound;
            if (rect.Width <= 0f)
                return;

            float param = (canvasX - rect.Left) / rect.Width;
            SeekParam = param < 0f ? 0f : (param > 1f ? 1f : param);
        }

        public override UIResponse RespondToMouseDown(GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            if (SeekBarMode != GraphSeekBarMode.Interactive || e.Button != MouseButtons.Left)
                return base.RespondToMouseDown(sender, e);

            // 動画の再生バーと同じく、プロット内のどこを押してもその位置へ飛ばし、
            // そのままドラッグへ移行する(ハンドルを掴む必要はない)
            if (IsSeekArea(e.CanvasLocation))
            {
                sender.Cursor = Cursors.SizeWE;
                grab_handle = true;
                SetSeekParamFromX(e.CanvasLocation.X);
                SeekParamChanged?.Invoke(SeekParam);
                return new UIResponse(GH_ObjectResponse.Capture);
            }
            return base.RespondToMouseDown(sender, e);
        }

        public override UIResponse RespondToMouseUp(GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            if (SeekBarMode != GraphSeekBarMode.Interactive)
                return base.RespondToMouseUp(sender, e);

            // 左ボタン以外を離してもドラッグは終わらせない(左を押したままのため)
            if (e.Button != MouseButtons.Left)
                return base.RespondToMouseUp(sender, e);

            if (grab_handle)
            {
                grab_handle = false;
                sender.Cursor = Cursors.Default;
                // 位置が確定してから重い処理を走らせる
                SeekCommitted?.Invoke(SeekParam);
                return new UIResponse(GH_ObjectResponse.Release);
            }
            return base.RespondToMouseUp(sender, e);
        }

        public override UIResponse RespondToMouseMove(GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            if (SeekBarMode != GraphSeekBarMode.Interactive)
                return base.RespondToMouseMove(sender, e);

            if (grab_handle)
            {
                sender.Cursor = Cursors.SizeWE;
                SetSeekParamFromX(e.CanvasLocation.X);
                SeekParamChanged?.Invoke(SeekParam);

                Owner.OnDisplayExpired();
                return new UIResponse(GH_ObjectResponse.Handled);
            }

            // 右ボタンなどでのドラッグには反応しない(ホバーは Button == None)
            if (e.Button != MouseButtons.None && e.Button != MouseButtons.Left)
            {
                ResetSeekCursor(sender);
                return base.RespondToMouseMove(sender, e);
            }

            if (IsSeekArea(e.CanvasLocation))
            {
                // シーク線の上は左右へ動かせることを、それ以外はその位置を
                // 指定できることを示す
                mouse_over_seek = true;
                sender.Cursor = Math.Abs(e.CanvasLocation.X - SeekPositionX) < SeekHandleGrabWidth
                    ? Cursors.SizeWE
                    : Cursors.Hand;
                return new UIResponse(GH_ObjectResponse.Handled);
            }

            ResetSeekCursor(sender);

            return base.RespondToMouseMove(sender, e);
        }

        /// <summary>
        /// シーク用に変えたカーソルを既定へ戻す。
        /// 領域外へ出たときに誰も戻してくれないので自前で行う。
        /// </summary>
        private void ResetSeekCursor(GH_Canvas sender)
        {
            if (!mouse_over_seek)
                return;

            mouse_over_seek = false;
            sender.Cursor = Cursors.Default;
        }

        /// <summary>
        /// X軸とY軸のラベルを設定します
        /// </summary>
        /// <param name="xLabel">X軸のラベル</param>
        /// <param name="yLabel">Y軸のラベル</param>
        public void SetAxisLabels(string xLabel, string yLabel)
        {
            XAxisLabel = xLabel ?? "";
            YAxisLabel = yLabel ?? "";
        }

    } // graphUI
}
