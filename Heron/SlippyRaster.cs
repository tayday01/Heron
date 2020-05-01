﻿using System;
using System.IO;
using System.Xml;
using System.Xml.Linq;
using System.Linq;
using System.Data;
using System.Drawing;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

using System.Windows.Forms;

using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Rhino;
using Rhino.Geometry;
using Rhino.DocObjects;
using Rhino.Collections;
using GH_IO;
using GH_IO.Serialization;

using Newtonsoft.Json.Bson;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Schema;
using Newtonsoft.Json.Serialization;

namespace Heron
{
    public class SlippyRaster : HeronComponent
    {
        /// <summary>
        /// Initializes a new instance of the SlippyRaster class.
        /// </summary>
        public SlippyRaster()
          : base("Slippy Raster", "Slippy Raster", "Get raster imagery from an tile-based map service", "GIS API")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {

            pManager.AddCurveParameter("Boundary", "boundary", "Boundary curve(s) for imagery", GH_ParamAccess.list);
            pManager.AddIntegerParameter("Zoom Level", "zoom", "Slippy map zoom level. Higher zoom level is higher resolution, but takes longer to download. Max zoom is typically 19.", GH_ParamAccess.item,14);
            pManager.AddTextParameter("File Location", "fileLoc", "Folder to place image files", GH_ParamAccess.item,@"C:\temp\");
            pManager.AddTextParameter("Prefix", "prefix", "Prefix for image file name", GH_ParamAccess.item, slippySource);
            //pManager.AddTextParameter("Slippy Raster URL", "slippyURL", "Slippy raster service to query", GH_ParamAccess.item);
            pManager.AddTextParameter("Slippy Access Header", "userAgent", "A user-agent header is sometimes required for access to Slippy resources, especially OSM. This can be any string.", GH_ParamAccess.item,"");
            pManager.AddBooleanParameter("Run", "get", "Go ahead and download imagery from the service", GH_ParamAccess.item, false);

            Message = SlippySource;


        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Image File", "Image", "File location of downloaded image", GH_ParamAccess.tree);
            pManager.AddCurveParameter("Image Frame", "imageFrame", "Bounding box of image for mapping to geometry", GH_ParamAccess.tree);
            pManager.AddIntegerParameter("Tile Count", "tileCount", "Number of image tiles to combine resulting from Slippy query", GH_ParamAccess.tree);

            pManager.AddTextParameter("Slippy Attribution", "slippyAtt", "Slippy word mark and text attribution if required by service", GH_ParamAccess.list);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            List<Curve> boundary = new List<Curve>();
            DA.GetDataList<Curve>(0, boundary);

            int zoom = -1;
            DA.GetData<int>(1, ref zoom);

            string fileloc = "";
            DA.GetData<string>(2, ref fileloc);
            if (!fileloc.EndsWith(@"\")) fileloc = fileloc + @"\";

            string prefix = "";
            DA.GetData<string>(3, ref prefix);

            string URL = slippyURL;
            //DA.GetData<string>(4, ref URL);

            string userAgent = "";
            DA.GetData<string>(4, ref userAgent);
 
            bool run = false;
            DA.GetData<bool>("Run", ref run);

            GH_Structure<GH_String> mapList = new GH_Structure<GH_String>();
            GH_Structure<GH_Curve> imgFrame = new GH_Structure<GH_Curve>();
            GH_Structure<GH_Integer> tCount = new GH_Structure<GH_Integer>();


            for (int i = 0; i <boundary.Count; i++)
            {
                GH_Path path = new GH_Path(i);
                

                ///Get image frame for given boundary and  make sure it's valid
                if (!boundary[i].GetBoundingBox(true).IsValid)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Boundary is not valid.");
                    return;
                }
                BoundingBox boundaryBox = boundary[i].GetBoundingBox(true);

                ///TODO: look into scaling boundary to get buffer tiles

                ///file path for final image
                string imgPath = fileloc + prefix + "_" + i + ".png";

                ///location of final image file
                mapList.Append(new GH_String(imgPath), path);

                ///create cache folder for images
                string cacheLoc = fileloc + @"HeronCache\";
                List<string> cacheFileLocs = new List<string>();
                if (!Directory.Exists(cacheLoc))
                {
                    Directory.CreateDirectory(cacheLoc);
                }

                ///tile bounding box array
                List<Point3d> boxPtList = new List<Point3d>();

                ///get the tile coordinates for all tiles within boundary
                List<List<int>> ranges = new List<List<int>>();
                ranges = Convert.GetTileRange(boundaryBox, zoom);

                List<List<int>> tileList = new List<List<int>>();
                List<int> x_range = ranges[0];
                List<int> y_range = ranges[1];

                ///cycle through tiles to get bounding box
                for (int y = y_range[0]; y <= y_range[1]; y++)
                {
                    for (int x = x_range[0]; x <= x_range[1]; x++)
                    {
                        ///add bounding box of tile to list
                        boxPtList.AddRange(Convert.GetTileAsPolygon(zoom, y, x).ToList());
                        cacheFileLocs.Add(cacheLoc + slippySource.Replace(" ", "") + zoom + x + y + ".png");
                    }
                }

                ///bounding box of tile boundaries
                BoundingBox bboxPts = new BoundingBox(boxPtList);

                ///convert bounding box to polyline
                List<Point3d> imageCorners = bboxPts.GetCorners().ToList();
                imageCorners.Add(imageCorners[0]);
                imgFrame.Append(new GH_Curve(new Rhino.Geometry.Polyline(imageCorners).ToNurbsCurve()), path);

                ///tile range as string for (de)serialization of TileCacheMeta
                string tileRangeString = zoom.ToString()
                    + x_range[0].ToString()
                    + y_range[0].ToString()
                    + x_range[1].ToString()
                    + y_range[1].ToString();

                ///check if the existing final image already covers the boundary. 
                ///if so, no need to download more or reassemble the cached tiles.
                if ((TileCacheMeta == tileRangeString) && Convert.CheckCacheImagesExist(cacheFileLocs))
                {
                    if (File.Exists(imgPath))
                    {
                        using (Bitmap imageT = new Bitmap(imgPath))
                        {

                            string imgComment = imageT.GetCommentsFromPNG();

                            imageT.Dispose();

                            ///check to see if tilerange in comments matches current tilerange
                            if (imgComment== (slippySource.Replace(" ", "") + tileRangeString))
                            {
                                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Using existing image.");
                                continue;
                            }

                        }

                    }

                }



                ///Query Slippy URL
                ///download all tiles within boundary
                ///merge tiles into one bitmap
                ///API to query
  

                ///Do the work of assembling image
                ///setup final image container bitmap
                int fImageW = (x_range[1] - x_range[0] + 1) * 256;
                int fImageH = (y_range[1] - y_range[0] + 1) * 256;
                Bitmap finalImage = new Bitmap(fImageW, fImageH);


                int imgPosW = 0;
                int imgPosH = 0;

                if (run == true)
                {
                    using (Graphics g = Graphics.FromImage(finalImage))
                    {
                        g.Clear(Color.Black);
                        for (int y = y_range[0]; y <= y_range[1]; y++)
                        {
                            for (int x = x_range[0]; x <= x_range[1]; x++)
                            {
                                //create tileCache name 
                                string tileCache = slippySource.Replace(" ","") + zoom + x + y + ".png";
                                string tileCahceLoc = cacheLoc + tileCache;
                                
                                //check cache folder to see if tile image exists locally
                                if (File.Exists(tileCahceLoc))
                                {
                                    Bitmap tmpImage = new Bitmap(Image.FromFile(tileCahceLoc));
                                    ///add tmp image to final
                                    g.DrawImage(tmpImage, imgPosW * 256, imgPosH * 256);
                                    tmpImage.Dispose();
                                }

                                else
                                {
                                    tileList.Add(new List<int> { zoom, y, x });
                                    string urlAuth = Convert.GetZoomURL(x, y, zoom, slippyURL);
                                    
                                    System.Net.WebClient client = new System.Net.WebClient();

                                    ///insert header if required
                                    client.Headers.Add("user-agent", userAgent);

                                    client.DownloadFile(urlAuth, tileCahceLoc);
                                    Bitmap tmpImage = new Bitmap(Image.FromFile(tileCahceLoc));
                                    client.Dispose();

                                    //add tmp image to final
                                    g.DrawImage(tmpImage, imgPosW * 256, imgPosH * 256);
                                    tmpImage.Dispose();
                                }

                                //increment x insert position, goes left to right
                                imgPosW++;
                            }
                            //increment y insert position, goes top to bottom
                            imgPosH++;
                            imgPosW = 0;

                        }
                        //garbage collection
                        g.Dispose();

                        //add tile range meta data to image comments
                        finalImage.AddCommentsToJPG(slippySource.Replace(" ", "") + tileRangeString);

                        //save the image
                        finalImage.Save(imgPath, System.Drawing.Imaging.ImageFormat.Jpeg);
                    }
                }

                //garbage collection
                finalImage.Dispose();


                //add to tile count total
                tCount.Append(new GH_Integer(tileList.Count), path);

                //write out new tile range metadata for serialization
                TileCacheMeta = tileRangeString;

            }


            DA.SetDataTree(0, mapList);
            DA.SetDataTree(1, imgFrame);
            DA.SetDataTree(2, tCount);
            DA.SetDataList(3, "copyright Slippy");

        }

        /////////////////////////





        ///Menu items
        ///https://www.grasshopper3d.com/forum/topics/closing-component-popup-side-bars-when-clicking-outside-the-form
        ///

        private bool IsServiceSelected(string serviceString)
        {
            return serviceString.Equals(slippySource);
        }

        protected override void AppendAdditionalComponentMenuItems(ToolStripDropDown menu)
        {

            if (slippySourceList =="")
            {
                slippySourceList = Convert.GetEnpoints();
            }

            JObject slippyJson = JObject.Parse(slippySourceList);

            ToolStripMenuItem root = new ToolStripMenuItem("Pick Slippy Raster Service");

            foreach (var service in slippyJson["Slippy Maps"])
            {
                string sName = service["service"].ToString();

                ToolStripMenuItem serviceName = new ToolStripMenuItem(sName);
                serviceName.Tag = sName;
                serviceName.Checked = IsServiceSelected(sName);
                //serviceName.ToolTipText = "Service description goes here";
                serviceName.Click += ServiceItemOnClick;

                root.DropDownItems.Add(serviceName);
            }
         
            menu.Items.Add(root);
          
            base.AppendAdditionalComponentMenuItems(menu);
            
        }

        private void ServiceItemOnClick (object sender, EventArgs e)
        {
            ToolStripMenuItem item = sender as ToolStripMenuItem;
            if (item == null)
                return;

            string code = (string)item.Tag;
            if (IsServiceSelected(code))
                return;

            RecordUndoEvent("SlippySource");
            RecordUndoEvent("SlippyURL");


            slippySource = code;
            slippyURL = JObject.Parse(slippySourceList)["Slippy Maps"].SelectToken("[?(@.service == '" + slippySource + "')].url").ToString();
            Message = slippySource;

            ExpireSolution(true);
        }

        

       

        ///Sticky parameters
        ///https://developer.rhino3d.com/api/grasshopper/html/5f6a9f31-8838-40e6-ad37-a407be8f2c15.htm
        ///

        private string tCacheMeta = "";
        private string slippySourceList = Convert.GetEnpoints();
        private string slippySource = JObject.Parse(Convert.GetEnpoints())["Slippy Maps"][0]["service"].ToString();
        private string slippyURL = JObject.Parse(Convert.GetEnpoints())["Slippy Maps"][0]["url"].ToString();


        public string TileCacheMeta
        {
            get { return tCacheMeta; }
            set
            {
                tCacheMeta = value;
                //Message = tCacheMeta;
            }
        }

        public string SlippySourceList
        {
            get { return slippySourceList; }
            set
            {
                slippySourceList = value;
            }
        }

        public string SlippySource
        {
            get { return slippySource; }
            set
            {
                slippySource = value;
                Message = slippySource;
            }
        }

        public string SlippyURL
        {
            get { return slippyURL; }
            set
            {
                slippyURL = value;
            }
        }

        public override bool Write(GH_IO.Serialization.GH_IWriter writer)
        {
            writer.SetString("TileCacheMeta", TileCacheMeta);
            writer.SetString("SlippySourceList", SlippySourceList);
            writer.SetString("SlippySource", SlippySource);
            writer.SetString("SlippyURL", SlippyURL);
            return base.Write(writer);
        }

        public override bool Read(GH_IReader reader)
        {
            TileCacheMeta = reader.GetString("TileCacheMeta");
            SlippySourceList = reader.GetString("SlippySourceList");
            SlippySource = reader.GetString("SlippySource");
            SlippyURL = reader.GetString("SlippyURL");
            return base.Read(reader);
        }


        /////////////////////////

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon
        {
            get
            {
                //You can add image files to your project resources and access them like this:
                // return Resources.IconForThisComponent;
                return Properties.Resources.raster;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("AF7AB46C-16A0-4C81-9363-24A9F940CE39"); }
        }
    }
}