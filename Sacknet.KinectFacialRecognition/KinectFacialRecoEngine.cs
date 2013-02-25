﻿using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using Emgu.CV;
using Emgu.CV.Structure;
using Microsoft.Kinect;
using Microsoft.Kinect.Toolkit.FaceTracking;

namespace Sacknet.KinectFacialRecognition
{
    /// <summary>
    /// A facial recognition engine using the Kinect facial tracking system and principal component analysis for recognition
    /// </summary>
    public class KinectFacialRecoEngine : IDisposable
    {
        private IFrameSource frameSource;

        private byte[] colorImageBuffer;
        private short[] depthImageBuffer;
        private int previousTrackedSkeletonId = -1;

        private FaceTracker faceTracker;

        /// <summary>
        /// Initializes a new instance of the KinectFacialRecoEngine class
        /// </summary>
        public KinectFacialRecoEngine(KinectSensor kinect, IFrameSource frameSource)
        {
            this.Kinect = kinect;
            this.ProcessingMutex = new object();
            this.frameSource = frameSource;
            this.frameSource.FrameDataUpdated += this.FrameSource_FrameDataUpdated;
        }

        /// <summary>
        /// Raised when recognition has been completed for a frame
        /// </summary>
        public event EventHandler<RecognitionResult> RecognitionComplete;

        /// <summary>
        /// Gets the active Kinect sensor
        /// </summary>
        protected KinectSensor Kinect { get; private set; }

        /// <summary>
        /// Gets a mutex that prevents the target faces from being updated during processing and vice-versa
        /// </summary>
        protected object ProcessingMutex { get; private set; }

        /// <summary>
        /// Gets the facial recognition engine
        /// </summary>
        protected EigenObjectRecognizer Recognizer { get; private set; }

        /// <summary>
        /// Loads the given target faces into the eigen object recognizer
        /// </summary>
        /// <param name="faces">The target faces to use for training.  Faces should be 100x100 and grayscale.</param>
        public virtual void SetTargetFaces(IEnumerable<TargetFace> faces)
        {
            this.SetTargetFaces(faces, 1750);
        }

        /// <summary>
        /// Loads the given target faces into the eigen object recognizer
        /// </summary>
        /// <param name="faces">The target faces to use for training.  Faces should be 100x100 and grayscale.</param>
        /// <param name="threshold">Eigen distance threshold for a match.  1500-2000 is a reasonable value.  0 will never match.</param>
        public virtual void SetTargetFaces(IEnumerable<TargetFace> faces, int threshold)
        {
            lock (this.ProcessingMutex)
            {
                if (this.Recognizer != null)
                {
                    this.Recognizer.Dispose();
                    this.Recognizer = null;
                }

                if (faces != null && faces.Any())
                {
                    var termCrit = new MCvTermCriteria(faces.Count(), 0.001);
                    this.Recognizer = new EigenObjectRecognizer(faces, threshold, ref termCrit);
                }
            }
        }

        /// <summary>
        /// Disposes the object
        /// </summary>
        public void Dispose()
        {
            this.frameSource.FrameDataUpdated -= this.FrameSource_FrameDataUpdated;
        }

        /// <summary>
        /// Performs recognition on a new frame of data
        /// </summary>
        private void FrameSource_FrameDataUpdated(object sender, FrameData e)
        {
            if (this.colorImageBuffer == null || this.colorImageBuffer.Length != e.ColorFrame.PixelDataLength)
                this.colorImageBuffer = new byte[e.ColorFrame.PixelDataLength];

            e.ColorFrame.CopyPixelDataTo(this.colorImageBuffer);

            if (this.depthImageBuffer == null || this.depthImageBuffer.Length != e.DepthFrame.PixelDataLength)
                this.depthImageBuffer = new short[e.DepthFrame.PixelDataLength];

            e.DepthFrame.CopyPixelDataTo(this.depthImageBuffer);

            if (e.TrackedSkeleton != null && e.TrackedSkeleton.TrackingState == SkeletonTrackingState.Tracked)
            {
                // Reset the face tracker if we lost our old skeleton...
                if (e.TrackedSkeleton.TrackingId != this.previousTrackedSkeletonId && this.faceTracker != null)
                    this.faceTracker.ResetTracking();

                if (this.faceTracker == null)
                {
                    try
                    {
                        this.faceTracker = new FaceTracker(this.Kinect);
                    }
                    catch (InvalidOperationException)
                    {
                        // During some shutdown scenarios the FaceTracker
                        // is unable to be instantiated.  Catch that exception
                        // and don't track a face.
                        this.faceTracker = null;
                    }
                }

                if (this.faceTracker != null)
                {
                    var faceTrackFrame = this.faceTracker.Track(
                        e.ColorFrame.Format,
                        this.colorImageBuffer,
                        e.DepthFrame.Format,
                        this.depthImageBuffer,
                        e.TrackedSkeleton);

                    if (faceTrackFrame.TrackSuccessful)
                    {
                        using (var originalBitmap = this.ImageToBitmap(this.colorImageBuffer, e.ColorFrame.Width, e.ColorFrame.Height))
                        {
                            var trackingResults = new TrackingResults
                            {
                                FacePoints = faceTrackFrame.GetProjected3DShape(),
                                FaceRect = faceTrackFrame.FaceRect
                            };

                            using (var result = this.Process(originalBitmap, trackingResults))
                            {
                                if (this.RecognitionComplete != null)
                                    this.RecognitionComplete(this, result);
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Attempt to find a trained face in the original bitmap
        /// </summary>
        private RecognitionResult Process(Bitmap originalBitmap, TrackingResults trackingResults)
        {
            lock (this.ProcessingMutex)
            {
                var result = new RecognitionResult();
                result.OriginalBitmap = originalBitmap;
                result.ProcessedBitmap = (Bitmap)originalBitmap.Clone();

                using (var origImage = new Image<Bgr, byte>(originalBitmap))
                {
                    using (var g = Graphics.FromImage(result.ProcessedBitmap))
                    {
                        // Create a path tracing the face and draw on the processed image
                        var origPath = new GraphicsPath();

                        foreach (var point in trackingResults.FaceBoundaryPoints().Select(x => this.TranslatePoint(x)))
                        {
                            origPath.AddLine(point, point);
                        }

                        origPath.CloseFigure();
                        g.DrawPath(new Pen(Color.Red, 2), origPath);

                        var minX = (int)origPath.PathPoints.Min(x => x.X);
                        var maxX = (int)origPath.PathPoints.Max(x => x.X);
                        var minY = (int)origPath.PathPoints.Min(x => x.Y);
                        var maxY = (int)origPath.PathPoints.Max(x => x.Y);
                        var width = maxX - minX;
                        var height = maxY - minY;

                        // Create a cropped path tracing the face...
                        var croppedPath = new GraphicsPath();

                        foreach (var point in trackingResults.FaceBoundaryPoints().Select(x => this.TranslatePoint(x)))
                        {
                            var croppedPoint = new System.Drawing.Point(point.X - minX, point.Y - minY);
                            croppedPath.AddLine(croppedPoint, croppedPoint);
                        }

                        croppedPath.CloseFigure();

                        // ...and create a cropped image to use for facial recognition
                        using (var croppedBmp = new Bitmap(width, height))
                        {
                            using (var croppedG = Graphics.FromImage(croppedBmp))
                            {
                                croppedG.FillRectangle(Brushes.Gray, 0, 0, width, height);
                                croppedG.SetClip(croppedPath);
                                croppedG.DrawImage(result.OriginalBitmap, minX * -1, minY * -1);
                            }

                            using (var croppedImage = new Image<Bgr, byte>(croppedBmp))
                            {
                                using (var croppedGrey = croppedImage.Convert<Gray, byte>().Resize(100, 100, Emgu.CV.CvEnum.INTER.CV_INTER_CUBIC))
                                {
                                    croppedGrey._EqualizeHist();

                                    string key = null;
                                    float eigenDistance = -1;

                                    if (this.Recognizer != null)
                                        key = this.Recognizer.Recognize(croppedGrey, out eigenDistance);

                                    // Save detection info
                                    result.Faces = new List<RecognitionResult.Face>()
                                    {
                                        new RecognitionResult.Face()
                                        {
                                            TrackingResults = trackingResults,
                                            EigenDistance = eigenDistance,
                                            GrayFace = croppedGrey.ToBitmap(),
                                            Key = key
                                        }
                                    };

                                    return result;
                                }
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Transforms a Kinect ColorImageFrame to a bitmap (why is this so hard?)
        /// </summary>
        private Bitmap ImageToBitmap(byte[] buffer, int width, int height)
        {
            Bitmap bmap = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
            BitmapData bmapdata = bmap.LockBits(new System.Drawing.Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, bmap.PixelFormat);
            IntPtr ptr = bmapdata.Scan0;
            Marshal.Copy(buffer, 0, ptr, buffer.Length);
            bmap.UnlockBits(bmapdata);
            return bmap;
        }

        /// <summary>
        /// Translates between kinect and drawing points
        /// </summary>
        private System.Drawing.Point TranslatePoint(Microsoft.Kinect.Toolkit.FaceTracking.PointF point)
        {
            return new System.Drawing.Point((int)point.X, (int)point.Y);
        }
    }
}
