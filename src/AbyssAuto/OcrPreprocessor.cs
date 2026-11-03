using System.Runtime.InteropServices;
using OpenCvSharp;

namespace AbyssAuto;

public static class OcrPreprocessor
{
    // Only OCR pixels change. Capture evidence, template matching and button colors stay raw.
    public static Bitmap Prepare(Bitmap crop,string filter,out string applied)
    {
        applied="original";
        if(filter=="none")return new Bitmap(crop);
        using var stream=new MemoryStream();crop.Save(stream,System.Drawing.Imaging.ImageFormat.Png);
        using var source=Cv2.ImDecode(stream.ToArray(),ImreadModes.Color);
        using var gray=new Mat();Cv2.CvtColor(source,gray,ColorConversionCodes.BGR2GRAY);
        var pixels=new byte[gray.Rows*gray.Cols];Marshal.Copy(gray.Data,pixels,0,pixels.Length);
        if(!DitherRules.IsDithered(pixels,gray.Cols,gray.Rows))return new Bitmap(crop);
        using var filtered=new Mat();
        if(filter=="dither-gold")
        {
            using var smooth=new Mat();using var hsv=new Mat();using var mask=new Mat();
            Cv2.GaussianBlur(source,smooth,new OpenCvSharp.Size(3,3),.85);
            Cv2.CvtColor(smooth,hsv,ColorConversionCodes.BGR2HSV);
            Cv2.InRange(hsv,new Scalar(5,120,70),new Scalar(40,255,255),mask);
            Cv2.BitwiseNot(mask,filtered);
        }
        else if(filter=="dither-smooth")Cv2.GaussianBlur(source,filtered,new OpenCvSharp.Size(5,5),1.2);
        else throw new InvalidDataException("알 수 없는 OCR 전처리 설정");
        applied=filter;
        Cv2.ImEncode(".png",filtered,out var bytes);
        using var encoded=new MemoryStream(bytes);using var bitmap=new Bitmap(encoded);
        return new Bitmap(bitmap);
    }
}
