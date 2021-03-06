using System;
using System.IO;
using System.Text;
using System.Runtime.InteropServices;
using AcHelper;
using Linq2Acad;
using ThCADCore.NTS;
using ThCADExtension;
using Dreambuild.AutoCAD;
using ThMEPEngineCore.CAD;
using ThMEPEngineCore.Engine;
using ThMEPEngineCore.Service;
using ThMEPEngineCore.BeamInfo;
using ThMEPEngineCore.Algorithm;
using ThMEPEngineCore.BeamInfo.Utils;
using System.Collections.Generic;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.PlottingServices;
using Autodesk.AutoCAD.ApplicationServices;
using ThMEPEngineCore.LaneLine;
using System.Text.RegularExpressions;
using System.Linq;
using DotNetARX;
using GeometryExtensions;
using ThMEPEngineCore.Diagnostics;
using NetTopologySuite.Geometries;
using NFox.Cad;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Diagnostics;

namespace ThMEPEngineCore.Test
{
    delegate string PrintJPG_dele(Point3d objStart, Point3d objEnd, string strPrintName, string strStyleName, string strImgName, int PaperSizeIndex);
    public class ThMEPEngineCoreTestApp : IExtensionApplication
    {
        public Point3d anchor1; // 打印选取的锚点1：左下角
        public Point3d anchor2; // 打印选取的锚点2:右上角
        public double measure_scale = 4;   // 缩放比例
        public int paper_index = 0; // 当前打印的图纸尺寸index[0,5]
        public int imgFileNum = 0;  // 当前打印的图纸名称

        public void Initialize()
        {
            //
        }

        public void Terminate()
        {
            //
        }

        public class CsvAnnoItem
        {
            // anno in .csv：将cad标注信息写到csv文件
            public int boxid;
            public int xmin;
            public int ymin;
            public int width;
            public int height;
            public String label;
            public bool isMultiple;
            public int paperIndex;
            public int rotation;
            public List<int> coords;
        }
        public class JsonBoxAnnoItem
        {
            // anno in .json：从json文件中读取每个标注的信息
            public int box_id { get; set; }
            public int class_index { get; set; }
            public String class_name { get; set; }
            public List<int> bbox { get; set; }
            public float score { get; set; }
        }
        public class AnnoListForOneImg
        {
            public List<CsvAnnoItem> AnnoList { get; set; }
        }
        public bool ExportAnno2CSV(String FileName, AnnoListForOneImg detail)
        {
            // 构建数据集：write annotations into a csv
            try
            {
                var annoList = detail.AnnoList;
                StringBuilder strColu = new StringBuilder();
                StringBuilder strValue = new StringBuilder();
                StreamWriter sw = new StreamWriter(new FileStream(FileName, FileMode.CreateNew), Encoding.GetEncoding("GB2312"));
                strColu.Append("Label,Xmin,Ymin,Width,Height,isMultiple,paperIndex,Rotation,Coords");
                sw.WriteLine(strColu);
                foreach (var dr in annoList)
                {
                    strValue.Remove(0, strValue.Length);//移出
                    strValue.Append(dr.label + ",");
                    strValue.Append(dr.xmin + ",");
                    strValue.Append(dr.ymin + ",");
                    strValue.Append(dr.width + ",");
                    strValue.Append(dr.height + ",");
                    strValue.Append(dr.isMultiple + ",");
                    strValue.Append(dr.paperIndex + ",");
                    strValue.Append(dr.rotation + ",");
                    foreach (int coord in dr.coords)
                    {
                        strValue.Append(coord + " ");
                    }
                    sw.WriteLine(strValue);
                }
                sw.Close();
                return true;
            }
            catch
            {
                return false;
            }
        }
        private int UCSCoordsToImg(double value, double originX, double originY, double height_img, bool isX)
        {
            double res = 0;
            if (isX)
            {
                res = (value - originX) / measure_scale;
            }
            else
            {
                res = (value - originY) / measure_scale;
                res = height_img - res;
            }
            return (int)res;
        }
        public void getBoxandText(Point3d pt1, Point3d pt2, String csvName)
        {
            // 构建数据集：搜索所有标注
            /* Param:
             *      Point3d pt1:左下角坐标
             *      Point3d pt2:右上角坐标
             *      String csvName:存储路径(不包含.csv后缀)
             */
            using (AcadDatabase acadDatabase = AcadDatabase.Active())
            {
                Point2d origin1 = new Point2d(Math.Min(pt1.X, pt2.X), Math.Min(pt1.Y, pt2.Y));// 左下角
                Point2d origin2 = new Point2d(Math.Max(pt1.X, pt2.X), Math.Max(pt1.Y, pt2.Y));// 右上角
                double width_window = Math.Abs(pt2.X - pt1.X);
                double height_window = Math.Abs(pt2.Y - pt1.Y);//window长宽不变，变的是img
                double ratio = height_window / width_window;

                double[] papersize_array_w = { 4000, 6000, 8000, 10000, 12000, 14000, 16000, 18000, 20000, 24000, 28000, 32000, 40000, 50000, 60000 };
                double[] papersize_array_h = { 3000, 4500, 6000, 7500, 9000, 10500, 12000, 13500, 15000, 18000, 21000, 24000, 30000, 40000, 45000 };
                double width_img = papersize_array_w[paper_index];
                double height_img = papersize_array_h[paper_index];


                // 获取polyline
                TypedValue[] recType = new TypedValue[]
                {
                 new TypedValue((int)DxfCode.Operator, "<and"),
                 new TypedValue((int)DxfCode.LayerName, "图纸识别"),
                 new TypedValue((int)DxfCode.Start, "LWPOLYLINE"),
                 new TypedValue((int)DxfCode.Operator, "and>")
                };
                SelectionFilter recfilt = new SelectionFilter(recType);

                var Select_res = Active.Editor.SelectCrossingWindow(pt1, pt2, recfilt);
                Active.Editor.WriteLine(Select_res.Value.Count);
                Console.WriteLine(Select_res.Value.Count);

                DBObjectCollection allentity = new DBObjectCollection();
                foreach (ObjectId objId in Select_res.Value.GetObjectIds())
                {
                    allentity.Add(acadDatabase.Element<Polyline>(objId));
                }

                List<CsvAnnoItem> annotation_list = new List<CsvAnnoItem>();

                TypedValue[] tvs = new TypedValue[]
                {
                 new TypedValue((int)DxfCode.Operator, "<and"),
                 new TypedValue((int)DxfCode.LayerName, "文字图层"),
                 new TypedValue((int)DxfCode.Start, "TEXT"),
                 new TypedValue((int)DxfCode.Operator, "and>")
                };
                SelectionFilter textfilt = new SelectionFilter(tvs);
                TypedValue[] tvs2 = new TypedValue[]
                {
                 new TypedValue((int)DxfCode.Operator, "<and"),
                 new TypedValue((int)DxfCode.LayerName, "文字图层"),
                 new TypedValue((int)DxfCode.Start, "MTEXT"),
                 new TypedValue((int)DxfCode.Operator, "and>")
                };
                SelectionFilter textfilt2 = new SelectionFilter(tvs2);

                foreach (Polyline rec in allentity)
                {
                    CsvAnnoItem temp_anno = new CsvAnnoItem();
                    temp_anno.coords = new List<int>();
                    int numofv = rec.NumberOfVertices;
                    if (numofv < 4) continue;
                    Point2d v1 = rec.GetPoint2dAt(0);// box左上角[minx,maxy]
                    Point2d v2 = rec.GetPoint2dAt(0);// box右下角[maxx,miny]

                    double minx, miny, maxx, maxy;
                    minx = v1.X; maxy = v1.Y; maxx = v2.X; miny = v2.Y;
                    double longest = 0;
                    temp_anno.rotation = 0;
                    for (int i = 0; i < numofv; i++)
                    {
                        Point2d temppoint = rec.GetPoint2dAt(i);
                        SegmentType segType = rec.GetSegmentType(i);
                        if (segType == 0)
                        {
                            LineSegment2d lineseg = rec.GetLineSegment2dAt(i);

                            if (lineseg.Length > longest && lineseg.StartPoint.Y <= lineseg.EndPoint.Y)
                            {
                                longest = lineseg.Length;
                                double delta_x = lineseg.EndPoint.X - lineseg.StartPoint.X;
                                double delta_y = lineseg.EndPoint.Y - lineseg.StartPoint.Y;
                                temp_anno.rotation = (int)(Math.Acos(delta_x / Math.Sqrt(delta_x * delta_x + delta_y * delta_y)) / 3.14 * 180);
                            }
                        }


                        temp_anno.coords.Add(UCSCoordsToImg(temppoint.X, origin1.X, origin1.Y, height_img, true));
                        temp_anno.coords.Add(UCSCoordsToImg(temppoint.Y, origin1.X, origin1.Y, height_img, false));
                        minx = Math.Min(temppoint.X, minx);
                        maxx = Math.Max(temppoint.X, maxx);
                        maxy = Math.Max(temppoint.Y, maxy);
                        miny = Math.Min(temppoint.Y, miny);
                    }

                    // DONE:文字筛选
                    Point3d tt1 = new Point3d(minx, maxy, pt1.Z);
                    Point3d tt2 = new Point3d(maxx, miny, pt1.Z);
                    var Select_text = Active.Editor.SelectCrossingWindow(tt1, tt2, textfilt);
                    if (Select_text.Value != null)
                    {
                        foreach (ObjectId objId in Select_text.Value.GetObjectIds())
                        {
                            DBText dBText = acadDatabase.Element<DBText>(objId);
                            temp_anno.label = dBText.TextString;
                            break;
                        }
                    }
                    else
                    {
                        Select_text = Active.Editor.SelectCrossingWindow(tt1, tt2, textfilt2);
                        if (Select_text.Value != null)
                        {
                            foreach (ObjectId objId in Select_text.Value.GetObjectIds())
                            {
                                MText dBText = acadDatabase.Element<MText>(objId);
                                temp_anno.label = dBText.Contents;
                                break;
                            }
                        }
                        else
                        {
                            Active.Editor.WriteLine("no text");
                            continue;
                        }
                    }

                    // DONE:box记录
                    double x1 = (minx - origin1.X) / measure_scale;//左上角
                    double y1 = (maxy - origin1.Y) / measure_scale;
                    double x2 = (maxx - origin1.X) / measure_scale;//右下角
                    double y2 = (miny - origin1.Y) / measure_scale;

                    temp_anno.width = (int)Math.Abs(x2 - x1);
                    temp_anno.height = (int)Math.Abs(y2 - y1);
                    temp_anno.xmin = (int)x1;
                    temp_anno.ymin = (int)(height_img - y1);
                    temp_anno.paperIndex = paper_index;
                    if (Select_text.Value.Count >= 1)
                    {
                        if (Select_text.Value.Count > 1) temp_anno.isMultiple = true;
                        else temp_anno.isMultiple = false;
                        annotation_list.Add(temp_anno);
                    }

                }

                AnnoListForOneImg annotempList = new AnnoListForOneImg();
                annotempList.AnnoList = annotation_list;
                Active.Editor.WriteLine(annotation_list.Count);
                ExportAnno2CSV(csvName + ".csv", annotempList);
                annotation_list.Clear();
            }
        }

        private void ExecuteCMD(String command)
        {

            Process p = new Process();
            p.StartInfo.FileName = "cmd.exe";
            p.StartInfo.UseShellExecute = false;//是否使用操作系统shell启动
            p.StartInfo.RedirectStandardInput = true;//接受来自调用程序的输入信息
            p.StartInfo.RedirectStandardOutput = true;//由调用程序获取输出信息
            p.StartInfo.RedirectStandardError = true;//重定向标准错误输出
            p.StartInfo.CreateNoWindow = true;//不显示程序窗口
            p.Start();//启动程序
            p.StandardInput.WriteLine("activate openmmlab");
            p.StandardInput.WriteLine("cd ../..");
            p.StandardInput.WriteLine("d:");
            p.StandardInput.WriteLine("cd D:\\ProgramData\\Anaconda3\\mmdetection\\myutils");   // FIXME: windows修改路径

            p.StandardInput.WriteLine(command);

            p.StandardInput.WriteLine("exit");

            p.WaitForExit();
            p.Close();

        }
        [CommandMethod("TIANHUACAD", "THDETECTTEST", CommandFlags.Modal)]
        public void THDETECTTEST()
        {
            using (AcadDatabase acadDatabase = AcadDatabase.Active())
            {

                // Detection: REST API
                // curl http://127.0.0.1:8080/predictions/swin -T examples/image_classifier/kitten.jpg
                // ExecuteCMD("D:\\ProgramData\\Anaconda3\\envs\\openmmlab\\python.exe D:\\ProgramData\\Anaconda3\\mmdetection\\myutils\\inference.py  -image_name "+Convert.ToString(imgFileNum));
                // Draw Box:
                Point3d pt1 = anchor1;
                Point3d pt2 = anchor2;

                Point2d origin1 = new Point2d(Math.Min(pt1.X, pt2.X), Math.Min(pt1.Y, pt2.Y));// 左下角
                Point2d origin2 = new Point2d(Math.Max(pt1.X, pt2.X), Math.Max(pt1.Y, pt2.Y));// 右上角
                double width_window = Math.Abs(pt2.X - pt1.X);
                double height_window = Math.Abs(pt2.Y - pt1.Y);
                double ratio = height_window / width_window;

                double[] papersize_array_w = { 4000, 6000, 8000, 10000, 12000, 14000, 16000, 18000, 20000, 24000, 28000, 32000, 40000, 50000, 60000 };
                double[] papersize_array_h = { 3000, 4500, 6000, 7500, 9000, 10500, 12000, 13500, 15000, 18000, 21000, 24000, 30000, 40000, 45000 };
                double width_img = papersize_array_w[paper_index];
                double height_img = papersize_array_h[paper_index];

                String[] classes = { "坐便器", "小便器", "蹲便器", "洗脸盆", "洗涤槽", "拖把池", "洗衣机", "地漏", "淋浴房", "淋浴房-转角型", "浴缸", "淋浴器" };
                System.IO.StreamReader file = System.IO.File.OpenText("d:\\THdetection\\label\\" + Convert.ToString(imgFileNum) + ".json");
                JsonTextReader reader = new JsonTextReader(file);
                JArray array = (JArray)JToken.ReadFrom(reader);
                List<JsonBoxAnnoItem> boxlist = array.ToObject<List<JsonBoxAnnoItem>>();
                foreach (JsonBoxAnnoItem box_anno in boxlist)
                {
                    String boxstring = "";
                    String label = classes[box_anno.class_index];
                    /*
                    foreach (int coord in box_anno.bbox)
                    {
                        boxstring += Convert.ToString(coord);
                    }
                    */
                    Point2dCollection point2Ds = new Point2dCollection();
                    double[] textLoc = { 0, 0 };
                    for (int i = 0; i < 8; i += 2)
                    {
                        int pt_X = box_anno.bbox[i];
                        int pt_Y = (int)(height_img) - box_anno.bbox[i + 1];
                        Point2d tmp_pt2d = new Point2d(pt_X * measure_scale + origin1.X, pt_Y * measure_scale + origin1.Y);
                        textLoc[0] += tmp_pt2d.X;
                        textLoc[1] += tmp_pt2d.Y;
                        point2Ds.Add(tmp_pt2d);
                    }
                    int pt_X0 = box_anno.bbox[0];
                    int pt_Y0 = (int)(height_img) - box_anno.bbox[1];
                    Point2d tmp_pt2d0 = new Point2d(pt_X0 * measure_scale + origin1.X, pt_Y0 * measure_scale + origin1.Y);
                    point2Ds.Add(tmp_pt2d0);
                    /*
                    int xmin, ymin, xmax, ymax;
                    xmin = box_anno.bbox[0];
                    ymax = (int)(height_img) - box_anno.bbox[1];
                    xmax = xmin + box_anno.bbox[2];
                    ymin = ymax - box_anno.bbox[3];
                    double minx = xmin * measure_scale + origin1.X;
                    double maxx = xmax * measure_scale + origin1.X;
                    double miny = ymin * measure_scale + origin1.Y;
                    double maxy = ymax * measure_scale + origin1.Y;

                    Point2d x1 = new Point2d(minx, miny);
                    Point2d x4 = new Point2d(maxx, maxy);
                    */
                    Active.Editor.WriteLine(label);
                    // Active.Editor.WriteLine(boxstring);
                    Polyline poly = new Polyline(); // Draw Polyline
                    PolylineTools.CreatePolyline(poly, point2Ds);
                    poly.ColorIndex = 4;
                    acadDatabase.ModelSpace.Add(poly);

                    var dBText = new MText();
                    dBText.Contents = label;
                    dBText.Location = new Point3d(textLoc[0] / 4, textLoc[1] / 4, 0);
                    dBText.Height = 40;
                    dBText.TextHeight = 40;
                    dBText.ColorIndex = 4;
                    acadDatabase.ModelSpace.Add(dBText);
                }
            }
        }

        [CommandMethod("TIANHUACAD", "THDETECT", CommandFlags.Modal)]
        public void THDETECT()
        {
            // 选择矩形区域
            // 打印指定区域jpg
            // 提取标注：得到所有识别图层的矩形
            // 提取标注：每一个矩形寻找内部文字
            using (AcadDatabase acadDatabase = AcadDatabase.Active())
            {
                double[] papersize_array_w = { 4000, 6000, 8000, 10000, 12000, 14000, 16000, 18000, 20000, 24000, 28000, 32000, 40000, 50000, 60000 };
                double[] papersize_array_h = { 3000, 4500, 6000, 7500, 9000, 10500, 12000, 13500, 15000, 18000, 21000, 24000, 30000, 40000, 45000 };

                // select a rectangle
                Point3d pt1 = Active.Editor.GetPoint("select left down point of rotated region: ").Value;
                Point3d pt2 = Active.Editor.GetPoint("select right up point of rotated region: ").Value;
                anchor1 = pt1;
                anchor2 = pt2;
                Point2d origin1 = new Point2d(Math.Min(pt1.X, pt2.X), Math.Min(pt1.Y, pt2.Y));// 左下角
                Point2d origin2 = new Point2d(Math.Max(pt1.X, pt2.X), Math.Max(pt1.Y, pt2.Y));// 右上角
                //Point2d origin1_rot = origin1.TransformBy(rotM);
                //Point2d origin2_rot = origin2.TransformBy(rotM);
                double width_window = Math.Abs(origin2.X - origin1.X);
                double height_window = Math.Abs(origin2.Y - origin1.Y);

                double ratio = height_window / width_window;
                double width_img = width_window / measure_scale;
                double height_img = height_window / measure_scale;
                Active.Editor.WriteLine(width_img);
                Active.Editor.WriteLine(height_img);
                bool find_paper = false;
                for (int i = 0; i < 15; i++)
                {   // 确定打印的图纸尺寸:实际图片范围长宽需要都小于图纸长宽
                    if (width_img <= papersize_array_w[i] && height_img <= papersize_array_h[i])
                    {
                        paper_index = i;
                        width_img = papersize_array_w[i];
                        height_img = papersize_array_h[i];
                        find_paper = true;
                        break;
                    }
                }
                if (!find_paper)
                {
                    paper_index = 14;
                    width_img = papersize_array_w[paper_index];
                    height_img = papersize_array_h[paper_index];
                }

                var pr = Active.Editor.GetInteger("Input a number as img name:");

                String RootFolder = "d:\\THdetection";
                String ImgFolder = "d:\\THdetection\\image";
                String LabelFolder = "d:\\THdetection\\label";
                if (Directory.Exists(RootFolder) == false)//如果不存在就创建file文件夹
                {
                    Directory.CreateDirectory(RootFolder);
                }
                if (Directory.Exists(ImgFolder) == false)//如果不存在就创建file文件夹
                {
                    Directory.CreateDirectory(ImgFolder);
                }
                if (Directory.Exists(LabelFolder) == false)//如果不存在就创建file文件夹
                {
                    Directory.CreateDirectory(LabelFolder);
                }

                String strFileName = ImgFolder;
                if (pr.Status != PromptStatus.OK)
                {
                    return;
                }
                imgFileNum = pr.Value;
                strFileName = ImgFolder + "\\" + Convert.ToString(pr.Value);
                String csvName = LabelFolder + "\\" + Convert.ToString(pr.Value);
                if (File.Exists(csvName + ".csv"))
                {
                    File.Delete(csvName + ".csv");
                }
                Stopwatch stopwatch = new Stopwatch();
                stopwatch.Start();
                // -- PRINT -- //
                Active.Editor.WriteLine("启动plot");
                Active.Editor.WriteLine(DateTime.Now.ToString("yyyyMMdd HH:mm:ss"));
                PrintJPG(pt1, pt2, "PublishToWeb JPG.pc3", "monochrome.ctb", strFileName, paper_index);

                Active.Editor.WriteLine("plot返回");
                Active.Editor.WriteLine(DateTime.Now.ToString("yyyyMMdd HH:mm:ss"));

                while (!File.Exists(strFileName + ".jpg")) { }
                Active.Editor.WriteLine("文件出现：");
                Active.Editor.WriteLine(DateTime.Now.ToString("yyyyMMdd HH:mm:ss"));
                /*
                while (PlotFactory.ProcessPlotState != ProcessPlotState.NotPlotting){}
                */
                stopwatch.Stop();
                TimeSpan timespan = stopwatch.Elapsed;
                Active.Editor.WriteLine("not plotting");
                Active.Editor.WriteLine(DateTime.Now.ToString("yyyyMMdd HH:mm:ss"));
                Active.Editor.WriteLine("Print Finished!");
                Active.Editor.WriteLine(timespan.TotalSeconds);
                Console.WriteLine("Print Finished!");
                //PrintJPG_dele printerDele = new PrintJPG_dele(PrintJPG);
                //printerDele.Invoke(pt1, pt2, "PublishToWeb JPG.pc3", "monochrome.ctb", strFileName, paper_index);

                // FIXME:如果要构建数据集就将下面这行去注释
                // getBoxandText(pt1, pt2, csvName);
                // DETECT
                Stopwatch stopwatch2 = new Stopwatch();
                stopwatch2.Start();
                ExecuteCMD("python infer_test.py  --image_name " + Convert.ToString(imgFileNum) + " --score_thres 0.6");
                stopwatch2.Stop();
                timespan = stopwatch2.Elapsed;
                Active.Editor.WriteLine(timespan.TotalSeconds);
                Active.Editor.WriteLine("cmd返回");
                Active.Editor.WriteLine(DateTime.Now.ToString("yyyyMMdd HH:mm:ss"));
                // 画图
                String[] classes = { "坐便器", "小便器", "蹲便器", "洗脸盆", "洗涤槽", "拖把池", "洗衣机", "地漏", "淋浴房", "淋浴房-转角型", "浴缸", "淋浴器" };
                System.IO.StreamReader file = System.IO.File.OpenText("d:\\THdetection\\label\\" + Convert.ToString(imgFileNum) + ".json");
                JsonTextReader reader = new JsonTextReader(file);
                JArray array = (JArray)JToken.ReadFrom(reader);
                List<JsonBoxAnnoItem> boxlist = array.ToObject<List<JsonBoxAnnoItem>>();
                foreach (JsonBoxAnnoItem box_anno in boxlist)
                {
                    String boxstring = "";
                    String label = classes[box_anno.class_index];
                    /*
                    foreach (int coord in box_anno.bbox)
                    {
                        boxstring += Convert.ToString(coord);
                    }
                    */
                    Point2dCollection point2Ds = new Point2dCollection();
                    double[] textLoc = { 0, 0 };
                    for (int i = 0; i < 8; i += 2)
                    {
                        int pt_X = box_anno.bbox[i];
                        int pt_Y = (int)(height_img) - box_anno.bbox[i + 1];
                        Point2d tmp_pt2d = new Point2d(pt_X * measure_scale + origin1.X, pt_Y * measure_scale + origin1.Y);
                        textLoc[0] += tmp_pt2d.X;
                        textLoc[1] += tmp_pt2d.Y;
                        point2Ds.Add(tmp_pt2d);
                    }
                    int pt_X0 = box_anno.bbox[0];
                    int pt_Y0 = (int)(height_img) - box_anno.bbox[1];
                    Point2d tmp_pt2d0 = new Point2d(pt_X0 * measure_scale + origin1.X, pt_Y0 * measure_scale + origin1.Y);
                    point2Ds.Add(tmp_pt2d0);
                    /*
                    int xmin, ymin, xmax, ymax;
                    xmin = box_anno.bbox[0];
                    ymax = (int)(height_img) - box_anno.bbox[1];
                    xmax = xmin + box_anno.bbox[2];
                    ymin = ymax - box_anno.bbox[3];
                    double minx = xmin * measure_scale + origin1.X;
                    double maxx = xmax * measure_scale + origin1.X;
                    double miny = ymin * measure_scale + origin1.Y;
                    double maxy = ymax * measure_scale + origin1.Y;

                    Point2d x1 = new Point2d(minx, miny);
                    Point2d x4 = new Point2d(maxx, maxy);
                    */
                    Active.Editor.WriteLine(label);
                    // Active.Editor.WriteLine(boxstring);
                    Polyline poly = new Polyline(); // Draw Polyline
                    PolylineTools.CreatePolyline(poly, point2Ds);
                    poly.ColorIndex = 4;
                    acadDatabase.ModelSpace.Add(poly);

                    var dBText = new MText();
                    dBText.Contents = label;
                    dBText.Location = new Point3d(textLoc[0] / 4, textLoc[1] / 4, 0);
                    dBText.Height = 40;
                    dBText.TextHeight = 40;
                    dBText.ColorIndex = 4;
                    acadDatabase.ModelSpace.Add(dBText);

                }
            }
        }

        private Extents2d Ucs2Dcs(Point3d objStart, Point3d objEnd)
        {

            ResultBuffer rbFrom =
                new ResultBuffer(new TypedValue(5003, 1)),
                rbTo =
                new ResultBuffer(new TypedValue(5003, 2));


            Point3d pt1 = Autodesk.AutoCAD.Internal.Utils.UcsToDisplay(objStart, false);
            Point3d pt2 = Autodesk.AutoCAD.Internal.Utils.UcsToDisplay(objEnd, false);
            Point2d pStart = new Point2d(pt1.X, pt1.Y);
            Point2d pEnd = new Point2d(pt2.X, pt2.Y);
            //设置打印范围
            Extents2d exWin = new Extents2d(pStart, pEnd);
            return exWin;
        }

        public string PrintJPG(Point3d objStart, Point3d objEnd, string strPrintName, string strStyleName, string strImgName, int PaperSizeIndex)
        {
            // 打开文档数据库
            Document acDoc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            Database acCurDb = acDoc.Database;
            Extents2d printAreaextent = Ucs2Dcs(objStart, objEnd);//获取打印范围
            string strFileName = string.Empty;

            using (Transaction acTrans = acCurDb.TransactionManager.StartTransaction())
            {
                BlockTableRecord btr =
                          (BlockTableRecord)acTrans.GetObject(
                            acCurDb.CurrentSpaceId,
                            OpenMode.ForRead
                          );

                Layout acLayout =
                    (Layout)acTrans.GetObject(
                        btr.LayoutId,
                    OpenMode.ForRead
                    );

                // Get the PlotInfo from the layout
                PlotInfo acPlInfo = new PlotInfo();
                acPlInfo.Layout = btr.LayoutId;

                // Get a copy of the PlotSettings from the layout
                PlotSettings acPlSet = new PlotSettings(acLayout.ModelType);
                acPlSet.CopyFrom(acLayout);

                // Update the PlotSettings object
                PlotSettingsValidator acPlSetVdr = PlotSettingsValidator.Current;

                acPlSetVdr.SetPlotWindowArea(acPlSet, printAreaextent); //设置打印范围
                // Set the plot type
                acPlSetVdr.SetPlotType(acPlSet,
                                       Autodesk.AutoCAD.DatabaseServices.PlotType.Window);

                // FIXME: Set the plot scale：待修正
                acPlSetVdr.SetUseStandardScale(acPlSet, true);
                StdScaleType stype = (StdScaleType)18;  // 1To4

                acPlSetVdr.SetStdScaleType(acPlSet, stype);

                // Center the plot
                acPlSetVdr.SetPlotCentered(acPlSet, false);
                Point2d temp_origin = new Point2d(0, 0);
                acPlSetVdr.SetPlotOrigin(acPlSet, temp_origin);


                // Set the plot device to use
                // acPlSetVdr.SetPlotConfigurationName(acPlSet, strPrintName);

                var devicelist = acPlSetVdr.GetPlotDeviceList();

                acPlSetVdr.SetPlotConfigurationName(acPlSet, strPrintName, null);
                acPlSetVdr.RefreshLists(acPlSet);

                var medialist = acPlSetVdr.GetCanonicalMediaNameList(acPlSet);
                foreach (var canonmedia in medialist)
                {
                    Active.Editor.WriteLine(canonmedia);
                    Console.WriteLine(canonmedia);
                }
                double[] papersize_array_w = { 4000, 6000, 8000, 10000, 12000, 14000, 16000, 18000, 20000, 24000, 28000, 32000, 40000, 50000, 60000 };
                double[] papersize_array_h = { 3000, 4500, 6000, 7500, 9000, 10500, 12000, 13500, 15000, 18000, 21000, 24000, 30000, 40000, 45000 };
                String[] media_array =
                {
                    "UserDefinedRaster (4000.00 x 3000.00像素)",  //0
                    "UserDefinedRaster (6000.00 x 4500.00像素)",  //0
                    "UserDefinedRaster (8000.00 x 6000.00像素)",      //1 
                    "UserDefinedRaster (10000.00 x 7500.00像素)",      //new
                    "UserDefinedRaster (12000.00 x 9000.00像素)",     //2
                    "UserDefinedRaster (14000.00 x 10500.00像素)",      //new 
                    "UserDefinedRaster (16000.00 x 12000.00像素)",    //3
                    "UserDefinedRaster (18000.00 x 13500.00像素)",      //new
                    "UserDefinedRaster (20000.00 x 15000.00像素)",    //4
                    "UserDefinedRaster (24000.00 x 18000.00像素)",    //5
                    "UserDefinedRaster (28000.00 x 21000.00像素)",      //new
                    "UserDefinedRaster (32000.00 x 24000.00像素)",    //6
                    "UserDefinedRaster (40000.00 x 30000.00像素)",    //7
                    "UserDefinedRaster (50000.00 x 40000.00像素)",     //8
                    "UserDefinedRaster (60000.00 x 45000.00像素)"      //new
                };
                Active.Editor.WriteLine(paper_index);
                String localmedia = media_array[PaperSizeIndex];
                Active.Editor.WriteLine(localmedia);
                acPlSetVdr.SetPlotConfigurationName(acPlSet, strPrintName, localmedia);

                acPlSetVdr.SetCurrentStyleSheet(acPlSet, strStyleName);

                acPlSetVdr.SetPlotRotation(acPlSet, PlotRotation.Degrees000);

                // Set the plot info as an override since it will
                // not be saved back to the layout
                acPlInfo.OverrideSettings = acPlSet;

                // Validate the plot info
                PlotInfoValidator acPlInfoVdr = new PlotInfoValidator();
                acPlInfoVdr.MediaMatchingPolicy = MatchingPolicy.MatchEnabled;
                acPlInfoVdr.Validate(acPlInfo);

                // Check to see if a plot is already in progress
                if (PlotFactory.ProcessPlotState == ProcessPlotState.NotPlotting)
                {
                    using (PlotEngine acPlEng = PlotFactory.CreatePublishEngine())
                    {
                        // Track the plot progress with a Progress dialog
                        PlotProgressDialog acPlProgDlg = new PlotProgressDialog(false,
                                                                                1,
                                                                                false);

                        using (acPlProgDlg)
                        {
                            // Define the status messages to display when plotting starts
                            acPlProgDlg.set_PlotMsgString(PlotMessageIndex.DialogTitle,
                                                          "");

                            acPlProgDlg.set_PlotMsgString(PlotMessageIndex.CancelJobButtonMessage,
                                                          "Cancel Job");

                            acPlProgDlg.set_PlotMsgString(PlotMessageIndex.CancelSheetButtonMessage,
                                                          "Cancel Sheet");

                            acPlProgDlg.set_PlotMsgString(PlotMessageIndex.SheetSetProgressCaption,
                                                          "Sheet Set Progress");

                            acPlProgDlg.set_PlotMsgString(PlotMessageIndex.SheetProgressCaption,
                                                          "正在生成" + acDoc.Name);

                            // Set the plot progress range
                            acPlProgDlg.LowerPlotProgressRange = 0;
                            acPlProgDlg.UpperPlotProgressRange = 100;
                            acPlProgDlg.PlotProgressPos = 0;
                            Active.Editor.WriteLine(PlotFactory.ProcessPlotState);
                            // Display the Progress dialog
                            acPlProgDlg.OnBeginPlot();
                            acPlProgDlg.IsVisible = true;
                            Active.Editor.WriteLine(PlotFactory.ProcessPlotState);
                            // Start to plot the layout
                            acPlEng.BeginPlot(acPlProgDlg, null);

                            string strTempPath = "d:";
                            // strFileName = Path.Combine(strTempPath,acDoc.Name.Substring(acDoc.Name.LastIndexOf("\\") + 1).Replace("dwg", "")+ DateTime.Now.ToString("yyyyMMddhhmmssfff") + "Compare" + ".jpg");
                            strFileName = strImgName + ".jpg";

                            // Define the plot output
                            acPlEng.BeginDocument(acPlInfo,
                                                  acDoc.Name,
                                                  null,
                                                  1,
                                                  true,
                                                  strFileName);

                            // Display information about the current plot
                            acPlProgDlg.set_PlotMsgString(PlotMessageIndex.Status,
                                                          "Plotting: " + acDoc.Name + " - " +
                                                          acLayout.LayoutName);

                            // Set the sheet progress range
                            acPlProgDlg.OnBeginSheet();
                            acPlProgDlg.LowerSheetProgressRange = 0;
                            acPlProgDlg.UpperSheetProgressRange = 100;
                            acPlProgDlg.SheetProgressPos = 0;

                            // Plot the first sheet/layout
                            PlotPageInfo acPlPageInfo = new PlotPageInfo();
                            acPlEng.BeginPage(acPlPageInfo,
                                              acPlInfo,
                                              true,
                                              null);

                            acPlEng.BeginGenerateGraphics(null);
                            Active.Editor.WriteLine(PlotFactory.ProcessPlotState);
                            acPlEng.EndGenerateGraphics(null);

                            // Finish plotting the sheet/layout
                            acPlEng.EndPage(null);
                            acPlProgDlg.SheetProgressPos = 100;
                            acPlProgDlg.OnEndSheet();

                            // Finish plotting the document
                            acPlEng.EndDocument(null);
                            Active.Editor.WriteLine("end doc");
                            Active.Editor.WriteLine(PlotFactory.ProcessPlotState);
                            // Finish the plot
                            acPlProgDlg.PlotProgressPos = 100;
                            acPlProgDlg.OnEndPlot();
                            acPlEng.EndPlot(null);

                            Active.Editor.WriteLine(PlotFactory.ProcessPlotState);
                            acPlEng.Destroy();
                            acPlProgDlg.Destroy();
                        }
                    }
                }
            }
            return strFileName;
        }

    }
}
