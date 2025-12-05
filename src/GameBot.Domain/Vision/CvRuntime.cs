using System;

namespace GameBot.Domain.Vision
{
    public static class CvRuntime
    {
        public static string GetOpenCvBuildInformation()
        {
            try
            {
                // Returns the OpenCV build information string from native library via OpenCvSharp
                return OpenCvSharp.Cv2.GetBuildInformation();
            }
            catch (Exception ex)
            {
                return $"unavailable: {ex.GetType().Name}: {ex.Message}";
            }
        }
    }
}
