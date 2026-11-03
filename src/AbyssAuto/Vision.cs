using OpenCvSharp;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using CvRect = OpenCvSharp.Rect;
namespace AbyssAuto;

public sealed record TextHit(string Text, Rectangle Bounds, string Source="raw");
public sealed record Detection(string Name, bool Passed, Rectangle Bounds, int Score, string Ocr,
    string? Image, double ImageScore, bool Color, string Reason, IReadOnlyList<TextHit>? OcrAttempts=null, long RecognitionMilliseconds=0);
public sealed class Vision(Configuration config)
{
    private readonly OcrEngine ocr = OcrEngine.TryCreateFromLanguage(new Language("ko-KR"))
        ?? throw new InvalidOperationException("Windows 한국어 OCR 언어 팩을 설치해주세요.");
    public static string Normalize(string value) => TextRules.Normalize(value);
    public static bool Matches(string actual,string expected,bool fuzzy=false) => TextRules.Matches(actual,expected,fuzzy);
    private async Task<List<TextHit>> Read(Bitmap frame, Rectangle area, int scale, CancellationToken token, bool whiteText=false, string filter="none")
    {
        using var crop=frame.Clone(area, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var prepared=OcrPreprocessor.Prepare(crop,filter,out var preprocessing);
        if(whiteText)
        {
            for(var y=0;y<prepared.Height;y++)for(var x=0;x<prepared.Width;x++)
            {
                var pixel=prepared.GetPixel(x,y);
                prepared.SetPixel(x,y,WhiteTextRules.IsGlyph(pixel.R,pixel.G,pixel.B)?System.Drawing.Color.Black:System.Drawing.Color.White);
            }
        }
        using var resized=new Bitmap(prepared,new System.Drawing.Size(prepared.Width*scale,prepared.Height*scale));
        using var stream=new MemoryStream(); resized.Save(stream,System.Drawing.Imaging.ImageFormat.Png); stream.Position=0;
        var decoder=await BitmapDecoder.CreateAsync(stream.AsRandomAccessStream());
        using var bitmap=await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8,BitmapAlphaMode.Premultiplied);
        var result=await ocr.RecognizeAsync(bitmap);
        token.ThrowIfCancellationRequested();
        var hits = result.Lines.Select(line => {
            var boxes=line.Words.Select(w=>w.BoundingRect).ToList();
            var left=boxes.Min(b=>b.Left)/scale+area.Left;
            var top=boxes.Min(b=>b.Top)/scale+area.Top;
            var right=boxes.Max(b=>b.Right)/scale+area.Left;
            var bottom=boxes.Max(b=>b.Bottom)/scale+area.Top;
            return new TextHit(line.Text,Rectangle.FromLTRB((int)Math.Floor(left),(int)Math.Floor(top),(int)Math.Ceiling(right),(int)Math.Ceiling(bottom)),$"{(whiteText?"white-mask+"+preprocessing:preprocessing)}-{scale}x");
        }).ToList();
        // OCR can group a complete menu row into one line; retain exact individual labels too.
        hits.AddRange(result.Lines.SelectMany(l=>l.Words).Select(word=> {
            var b=word.BoundingRect;
            return new TextHit(word.Text,Rectangle.FromLTRB((int)Math.Floor(b.Left/scale)+area.Left,(int)Math.Floor(b.Top/scale)+area.Top,(int)Math.Ceiling(b.Right/scale)+area.Left,(int)Math.Ceiling(b.Bottom/scale)+area.Top),$"{(whiteText?"white-mask+"+preprocessing:preprocessing)}-{scale}x");
        }));
        return hits;
    }
    public async Task<Detection> Detect(Bitmap frame,string name,CancellationToken token)
    {
        var timer=System.Diagnostics.Stopwatch.StartNew();
        token.ThrowIfCancellationRequested();
        if(frame.Width!=config.Scenario.Width || frame.Height!=config.Scenario.Height) throw new InvalidOperationException("캡처 크기가 설정과 다릅니다.");
        var target=config.Targets[name]; var area=target.Region.Rectangle;
        var ocrArea=target.OcrRegion?.Rectangle ?? area;
        var lines=target.Text.Length==0 ? new List<TextHit>() : await Read(frame,ocrArea,1,token,filter:target.OcrFilter);
        bool Match(TextHit h)=>target.Text.Any(t=>Matches(h.Text,t,target.Fuzzy));
        bool HasEvidence()=>RecognitionRules.HasTextEvidence(lines.Select(l=>l.Text),target.Text,target.RequiredText,target.Fuzzy);
        if (target.Text.Length>0 && (!HasEvidence() || name=="menu" && MenuCount(lines)<3))
            lines.AddRange(await Read(frame,ocrArea,2,token,filter:target.OcrFilter));
        if(target.WhiteTextFallback && target.Text.Length>0 && !HasEvidence())
            lines.AddRange(await Read(frame,ocrArea,2,token,whiteText:true,filter:target.OcrFilter));
        if(target.OcrFilter=="dither-gold" && !HasEvidence())
            lines.AddRange(await Read(frame,ocrArea,2,token,filter:"dither-smooth"));
        var textHit=lines.FirstOrDefault(Match);
        if(name=="menu")
        {
            var count=MenuCount(lines);
            return new(name,count>=3,area,count,string.Join(" | ",lines.Select(l=>l.Text).Distinct()),null,0,false,$"서로 다른 메뉴 {count}개",lines.ToArray(),timer.ElapsedMilliseconds);
        }
        Rectangle imageBounds=Rectangle.Empty; double imageScore=0; string? imageName=null;
        using var png=new MemoryStream(); frame.Save(png,System.Drawing.Imaging.ImageFormat.Png);
        using var full=Cv2.ImDecode(png.ToArray(),ImreadModes.Color);
        using var roi=new Mat(full,new CvRect(area.X,area.Y,area.Width,area.Height));
        foreach(var file in target.Images)
        {
            var path=Path.Combine(config.Root,"images",file);
            if(!File.Exists(path)) continue;
            using var template=Cv2.ImRead(path,ImreadModes.Color);
            if(template.Empty() || template.Width<12 || template.Height<12 || template.Width>roi.Width || template.Height>roi.Height) continue;
            Cv2.MeanStdDev(template,out _,out var deviation);
            if(deviation.Val0+deviation.Val1+deviation.Val2<15) continue;
            using var scores=new Mat(); Cv2.MatchTemplate(roi,template,scores,TemplateMatchModes.CCoeffNormed);
            Cv2.MinMaxLoc(scores,out _,out var best,out _,out var position);
            if(double.IsFinite(best) && best>imageScore) { imageScore=best; imageName=file; imageBounds=new(area.X+position.X,area.Y+position.Y,template.Width,template.Height); }
        }
        var imagePassed=imageScore>=target.Threshold && imageName!=null;
        var textPassed=textHit!=null && HasEvidence();
        var conflict=imagePassed && textPassed && !imageBounds.IntersectsWith(textHit!.Bounds);
        var candidate=imagePassed ? imageBounds : textHit?.Bounds ?? Rectangle.Empty;
        bool color=false;
        if(!candidate.IsEmpty && target.Color!="none")
        {
            var expanded=new Rectangle(candidate.Left-(int)(candidate.Width*.4),candidate.Top-(int)(candidate.Height*.3),(int)(candidate.Width*1.8),(int)(candidate.Height*1.6));
            expanded.Intersect(area);
            using var pixels=new Mat(full,new CvRect(expanded.X,expanded.Y,expanded.Width,expanded.Height));
            var channels=Cv2.Split(pixels);
            try {
                double Median(Mat m) { var values=new byte[m.Rows*m.Cols]; for(int y=0;y<m.Rows;y++) for(int x=0;x<m.Cols;x++) values[y*m.Cols+x]=m.At<byte>(y,x); Array.Sort(values); return values[values.Length/2]; }
                var b=Median(channels[0]);var g=Median(channels[1]);var r=Median(channels[2]);
                color=g-Math.Max(r,b)>=60 || target.Color=="greenOrBlue" && b-Math.Max(r,g)>=60;
            } finally { foreach(var channel in channels) channel.Dispose(); }
        }
        var score=(textPassed?2:0)+(imagePassed?2:0)+(color?1:0);
        var passed=!conflict && !candidate.IsEmpty && score>=target.MinimumScore && (!target.RequireText||textPassed) && (!target.RequireImage||imagePassed) && (!target.RequireColor||color);
        return new(name,passed,candidate,score,string.Join(" | ",lines.Select(l=>l.Text).Distinct()),imageName,imageScore,color,conflict?"OCR과 사진 위치 충돌":passed?"합격":textHit!=null && !HasEvidence()?"추가 문구 근거 부족":"근거 부족",lines.ToArray(),timer.ElapsedMilliseconds);
    }
    private static int MenuCount(List<TextHit> lines)
    {
        string[] names=["캐릭터","가방","크래프팅","연금술","퀘스트","아르바이트","미션","거래소","환경설정"];
        return names.Count(name=>lines.Any(line=>Normalize(line.Text).Equals(name,StringComparison.Ordinal)));
    }
}
