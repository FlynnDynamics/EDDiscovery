﻿/*
 * Copyright 2019-2021 Robbyxp1 @ github.com
 *
 * Licensed under the Apache License, Version 2.0 (the "License"); you may not use this
 * file except in compliance with the License. You may obtain a copy of the License at
 *
 * http://www.apache.org/licenses/LICENSE-2.0
 * 
 * Unless required by applicable law or agreed to in writing, software distributed under
 * the License is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF
 * ANY KIND, either express or implied. See the License for the specific language
 * governing permissions and limitations under the License.
 */

using EliteDangerousCore;
using EliteDangerousCore.EDSM;
using GLOFC;
using GLOFC.Controller;
using GLOFC.GL4;
using GLOFC.GL4.Controls;
using OpenTK;
using OpenTK.Graphics;
using OpenTK.Graphics.OpenGL4;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace EDDiscovery.UserControls.Map3D
{
    public interface MapSaver           // saver interface
    {
        void PutSetting<T>(string id, T value);
        T GetSetting<T>(string id, T defaultvalue);
    }

    public class Map
    {
        public GLMatrixCalc matrixcalc;
        public Controller3D gl3dcontroller;
        public GLControlDisplay displaycontrol;
        public enum Parts
        {
            Galaxy = 1, GalObjects = 2, Regions = 4, Grid = 8, StarDots = 32, TravelPath = 64, EDSMStars = 128, RightClick = 256,
            SearchBox = 2048, GalaxyResetPos = 4096, PerspectiveChange = 8192, YHoldButton = 16384, AllowAutoEDSMStarsUpdate = 32768,
            PrepopulateEDSMLocalArea = 65536,
            All = -1
        }

        private Parts parts;

        private GLOFC.WinForm.GLWinFormControl glwfc;

        private GLRenderProgramSortedList rObjects = new GLRenderProgramSortedList();
        private GLItemsList items = new GLItemsList();

        private Vector4[] volumetricboundingbox;
        private GLVolumetricUniformBlock volumetricblock;
        private GLRenderableItem galaxyrenderable;
        private GalaxyShader galaxyshader;

        private GLShaderPipeline gridtextshader;
        private GLShaderPipeline gridshader;
        private GLRenderableItem gridrenderable;

        private TravelPath travelpath;
        private MapMenu galaxymenu;

        public GalacticMapping edsmmapping;

        private GalMapObjects galmapobjects;
        private GalMapRegions edsmgalmapregions;
        private GalMapRegions elitemapregions;
        
        private GalaxyStarDots stardots;
        private GLPointSpriteShader starsprites;
        private GalaxyStars galaxystars;

        private GLContextMenu rightclickmenu;

        //private GLBuffer debugbuffer;

        // global buffer blocks used
        private const int volumenticuniformblock = 2;
        private const int findstarblock = 3;
        private const int findgeomapblock = 4;
        private const int findgalaxystars = 5;

        private System.Diagnostics.Stopwatch hptimer = new System.Diagnostics.Stopwatch();

        public Map()
        {
        }

        public void Dispose()
        {
            if (galaxystars != null)
                galaxystars.Stop();
            items.Dispose();
        }

        public ulong ElapsedTimems { get { return glwfc.ElapsedTimems; } }

        #region Initialise

        public void Start(GLOFC.WinForm.GLWinFormControl glwfc, GalacticMapping edsmmapping, GalacticMapping eliteregions, UserControlCommonBase parent, Parts parts)
        {
            this.parts = parts;
            this.glwfc = glwfc;
            this.edsmmapping = edsmmapping;

            hptimer.Start();

            Bitmap galaxybitmap = BaseUtils.Icons.IconSet.Instance.Get("GalMap.Galaxy_L180") as Bitmap;

            items.Add(new GLMatrixCalcUniformBlock(), "MCUB");     // create a matrix uniform block 

            int lyscale = 1;
            int front = -20000 / lyscale, back = front + 90000 / lyscale, left = -45000 / lyscale, right = left + 90000 / lyscale, vsize = 2000 / lyscale;

            if ((parts & Parts.Galaxy ) != 0) // galaxy
            {
                volumetricboundingbox = new Vector4[]
                    {
                        new Vector4(left,-vsize,front,1),
                        new Vector4(left,vsize,front,1),
                        new Vector4(right,vsize,front,1),
                        new Vector4(right,-vsize,front,1),

                        new Vector4(left,-vsize,back,1),
                        new Vector4(left,vsize,back,1),
                        new Vector4(right,vsize,back,1),
                        new Vector4(right,-vsize,back,1),
                    };


                const int gnoisetexbinding = 3;     //tex bindings are attached per shaders so are not global
                const int gdisttexbinding = 4;
                const int galtexbinding = 1;

                volumetricblock = new GLVolumetricUniformBlock(volumenticuniformblock);
                items.Add(volumetricblock, "VB");

                int sc = 1;
                GLTexture3D noise3d = new GLTexture3D(1024 * sc, 64 * sc, 1024 * sc, OpenTK.Graphics.OpenGL4.SizedInternalFormat.R32f); // red channel only
                items.Add(noise3d, "Noise");
                ComputeShaderNoise3D csn = new ComputeShaderNoise3D(noise3d.Width, noise3d.Height, noise3d.Depth, 128 * sc, 16 * sc, 128 * sc, gnoisetexbinding);       // must be a multiple of localgroupsize in csn
                csn.StartAction += (A, m) => { noise3d.BindImage(gnoisetexbinding); };
                csn.Run();      // compute noise

                GLTexture1D gaussiantex = new GLTexture1D(1024, OpenTK.Graphics.OpenGL4.SizedInternalFormat.R32f); // red channel only
                items.Add(gaussiantex, "Gaussian");

                // set centre=width, higher widths means more curve, higher std dev compensate.
                // fill the gaussiantex with data
                ComputeShaderGaussian gsn = new ComputeShaderGaussian(gaussiantex.Width, 2.0f, 2.0f, 1.4f, gdisttexbinding);
                gsn.StartAction += (A, m) => { gaussiantex.BindImage(gdisttexbinding); };
                gsn.Run();      // compute noise

                GL.MemoryBarrier(MemoryBarrierFlags.AllBarrierBits);

                // load one upside down and horz flipped, because the volumetric co-ords are 0,0,0 bottom left, 1,1,1 top right
                GLTexture2D galtex = new GLTexture2D(galaxybitmap, SizedInternalFormat.Rgba8);
                items.Add(galtex, "galtex");
                galaxyshader = new GalaxyShader(volumenticuniformblock, galtexbinding, gnoisetexbinding, gdisttexbinding);
                items.Add(galaxyshader, "Galaxy-sh");
                // bind the galaxy texture, the 3dnoise, and the gaussian 1-d texture for the shader
                galaxyshader.StartAction += (a, m) => { galtex.Bind(galtexbinding); noise3d.Bind(gnoisetexbinding); gaussiantex.Bind(gdisttexbinding); };      // shader requires these, so bind using shader

                GLRenderState rt = GLRenderState.Tri();
                galaxyrenderable = GLRenderableItem.CreateNullVertex(OpenTK.Graphics.OpenGL4.PrimitiveType.Points, rt);   // no vertexes, all data from bound volumetric uniform, no instances as yet
                rObjects.Add(galaxyshader, "galshader", galaxyrenderable);
            }

            if ((parts & Parts.Regions) != 0)
            {
                var corr = new GalMapRegions.ManualCorrections[] {          // nerf the centeroid position slightly
                    new GalMapRegions.ManualCorrections("The Galactic Aphelion", y: -2000 ),
                    new GalMapRegions.ManualCorrections("The Abyss", y: +3000 ),
                    new GalMapRegions.ManualCorrections("Eurus", y: -3000 ),
                    new GalMapRegions.ManualCorrections("The Perseus Transit", x: -3000, y: -3000 ),
                    new GalMapRegions.ManualCorrections("Zephyrus", x: 0, y: 2000 ),
                };

                edsmgalmapregions = new GalMapRegions();
                edsmgalmapregions.CreateObjects("edsmregions", items, rObjects, edsmmapping, 8000, corr: corr);
            }

            if ((parts & Parts.Regions) != 0)
            {
                elitemapregions = new GalMapRegions();
                elitemapregions.CreateObjects("eliteregions", items, rObjects, eliteregions, 8000);
                EliteRegionsEnable = false;
            }

            if ((parts & Parts.StarDots) != 0)
            {
                int gran = 8;
                Bitmap heat = galaxybitmap.Function(galaxybitmap.Width / gran, galaxybitmap.Height / gran, mode: BitMapHelpers.BitmapFunction.HeatMap);
                //heat.Save(@"c:\code\heatmap.jpg", System.Drawing.Imaging.ImageFormat.Jpeg);

                Random rnd = new Random(23);

                GLBuffer buf = new GLBuffer(16 * 350000);     // since RND is fixed, should get the same number every time.
                buf.StartWrite(0); // get a ptr to the whole schebang

                int xcw = (right - left) / heat.Width;
                int zch = (back - front) / heat.Height;

                int points = 0;

                for (int x = 0; x < heat.Width; x++)
                {
                    for (int z = 0; z < heat.Height; z++)
                    {
                        int i = heat.GetPixel(x, z).R;
                        if (i > 32)
                        {
                            int gx = left + x * xcw;
                            int gz = front + z * zch;

                            float dx = (float)Math.Abs(gx) / 45000;
                            float dz = (float)Math.Abs(25889 - gz) / 45000;
                            double d = Math.Sqrt(dx * dx + dz * dz);     // 0 - 0.1412
                            d = 1 - d;  // 1 = centre, 0 = unit circle
                            d = d * 2 - 1;  // -1 to +1
                            double dist = ObjectExtensionsNumbersBool.GaussianDist(d, 1, 1.4);

                            int c = Math.Min(Math.Max(i * i * i / 120000, 1), 40);
                            //int c = Math.Min(Math.Max(i * i * i / 24000000, 1), 40);

                            dist *= 2000 / lyscale;
                            //System.Diagnostics.Debug.WriteLine("{0} {1} : dist {2} c {3}", x, z, dist, c);
                            //System.Diagnostics.Debug.Write(c);
                            GLPointsFactory.RandomStars4(buf, c, gx, gx + xcw, gz, gz + zch, (int)dist, (int)-dist, rnd, w: 0.8f);
                            points += c;
                            System.Diagnostics.Debug.Assert(points < buf.Length / 16);
                        }
                    }
                    //System.Diagnostics.Debug.WriteLine(".");
                }

                buf.StopReadWrite();

                stardots = new GalaxyStarDots();
                items.Add(stardots);
                GLRenderState rc = GLRenderState.Points(1);
                rc.DepthTest = false; // note, if this is true, there is a wierd different between left and right in view.. not sure why
                rObjects.Add(stardots, "stardots", GLRenderableItem.CreateVector4(items, OpenTK.Graphics.OpenGL4.PrimitiveType.Points, rc, buf, points));
                System.Diagnostics.Debug.WriteLine("Stars " + points);
            }

            rObjects.Add(new GLOperationClearDepthBuffer()); // clear depth buffer and now use full depth testing on the rest

            if ((parts & Parts.StarDots) != 0)
            {
                Bitmap starflare = BaseUtils.Icons.IconSet.Instance.Get("GalMap.StarFlare2") as Bitmap;
                items.Add(new GLTexture2D(starflare, SizedInternalFormat.Rgba8), "lensflare");
                starsprites = new GLPointSpriteShader(items.Tex("lensflare"), 64, 40);
                items.Add(starsprites, "PS");
                var p = GLPointsFactory.RandomStars4(1000, 0, 25899 / lyscale, 10000 / lyscale, 1000 / lyscale, -1000 / lyscale);
                GLRenderState rps = GLRenderState.PointSprites();
                rObjects.Add(starsprites, "starsprites", GLRenderableItem.CreateVector4Color4(items, OpenTK.Graphics.OpenGL4.PrimitiveType.Points, rps, p, new Color4[] { Color.White }));
            }

            if ((parts & Parts.Grid) != 0)
            {
                var gridvertshader = new DynamicGridVertexShader(Color.Cyan);
                items.Add(gridvertshader, "PLGRIDVertShader");
                var gridfragshader = new GLPLFragmentShaderVSColor();
                items.Add(gridfragshader, "PLGRIDFragShader");
                gridshader = new GLShaderPipeline(gridvertshader, gridfragshader);
                items.Add(gridshader, "DYNGRID");

                GLRenderState rl = GLRenderState.Lines(1);
                gridrenderable = GLRenderableItem.CreateNullVertex(OpenTK.Graphics.OpenGL4.PrimitiveType.Lines, rl, drawcount: 2);
                rObjects.Add(gridshader, "DYNGRIDRENDER", gridrenderable);
            }

            if ((parts & Parts.Grid) != 0)
            {
                var gridtextlabelsvertshader = new DynamicGridCoordVertexShader();
                items.Add(gridtextlabelsvertshader, "PLGRIDBitmapVertShader");
                var gridtextfragshader = new GLPLFragmentShaderTexture2DIndexed(0);
                items.Add(gridtextfragshader, "PLGRIDBitmapFragShader");
                gridtextshader = new GLShaderPipeline(gridtextlabelsvertshader, gridtextfragshader);
                items.Add(gridtextshader, "DYNGRIDBitmap");

                GLRenderState rl = GLRenderState.Tri(cullface: false);
                GLTexture2DArray gridtexcoords = new GLTexture2DArray();
                items.Add(gridtexcoords, "PLGridBitmapTextures");
                rObjects.Add(gridtextshader, "DYNGRIDBitmapRENDER", GLRenderableItem.CreateNullVertex(OpenTK.Graphics.OpenGL4.PrimitiveType.TriangleStrip, rl, drawcount: 4, instancecount: 9));
            }

            float sunsize = 1.0f;
            if ((parts & Parts.TravelPath) != 0)
            {
                travelpath = new TravelPath(50000, sunsize, 0.4f, findstarblock, true, items, rObjects );
                travelpath.CreatePath(parent.discoveryform.history);
                travelpath.SetSystem(parent.discoveryform.history.LastSystem);
            }

            if ((parts & Parts.GalObjects) != 0)
            {
                galmapobjects = new GalMapObjects();
                galmapobjects.CreateObjects(items, rObjects, edsmmapping, findgeomapblock, true);
            }

            if ((parts & Parts.EDSMStars) != 0)
            {
                galaxystars = new GalaxyStars(items, rObjects, sunsize, findgalaxystars);
                UpdateLocalArea(parent.discoveryform.history);
            }

            if ((parts & Parts.RightClick ) != 0)
            {
                rightclickmenu = new GLContextMenu("RightClickMenu",
                    new GLMenuItem("RCMInfo", "Information")
                    {
                        MouseClick = (s, e) => {
                            var nl = NameLocationDescription(rightclickmenu.Tag);
                            System.Diagnostics.Debug.WriteLine($"Info {nl.Item1} {nl.Item2}");
                            // logical name is important as menu uses it to close down
                            GLMessageBox msg = new GLMessageBox("InfoBoxForm-1", displaycontrol, e.WindowLocation, null,
                                    nl.Item3, $"{nl.Item1} @ {nl.Item2.X:0.##},{nl.Item2.Y:0.##},{nl.Item2.Z:0.##}", GLMessageBox.MessageBoxButtons.OK, null, Color.FromArgb(220, 60, 60, 70), Color.DarkOrange);
                        }
                    },
                    new GLMenuItem("RCMZoomIn", "Goto Zoom In")
                    {
                        MouseClick = (s1, e1) => {
                            var nl = NameLocationDescription(rightclickmenu.Tag);
                            gl3dcontroller.SlewToPositionZoom(nl.Item2, 300, -1);
                        }
                    },
                    new GLMenuItem("RCMGoto", "Goto Position")
                    {
                        MouseClick = (s1, e1) => {
                            var nl = NameLocationDescription(rightclickmenu.Tag);
                            System.Diagnostics.Debug.WriteLine($"Goto {nl.Item1} {nl.Item2}");
                            gl3dcontroller.SlewToPosition(nl.Item2, -1);
                        }
                    },
                    new GLMenuItem("RCMLookAt", "Look At")
                    {
                        MouseClick = (s1, e1) => {
                            var nl = NameLocationDescription(rightclickmenu.Tag);
                            gl3dcontroller.PanTo(nl.Item2, -1);
                        }
                    },
                    new GLMenuItem("RCMViewStarDisplay", "Display system")
                    {
                        MouseClick = (s1, e1) => {
                            ISystem s = rightclickmenu.Tag is HistoryEntry ? ((HistoryEntry)rightclickmenu.Tag).System : (ISystem)rightclickmenu.Tag;
                            ScanDisplayForm.ShowScanOrMarketForm(parent.FindForm(), s, true, parent.discoveryform.history, 0.8f, System.Drawing.Color.Purple);
                        }
                    },
                    new GLMenuItem("RCMViewEDSM", "View on EDSM")
                    {
                        MouseClick = (s1, e1) => {
                            ISystem s = rightclickmenu.Tag is HistoryEntry ? ((HistoryEntry)rightclickmenu.Tag).System : (ISystem)rightclickmenu.Tag;

                            EDSMClass edsm = new EDSMClass();
                            if (!edsm.ShowSystemInEDSM(s.Name))
                                ExtendedControls.MessageBoxTheme.Show(parent.FindForm(), "System could not be found - has not been synched or EDSM is unavailable");
                        }
                    }
                );

                rightclickmenu.Opening += (ms) => {
                    ms["RCMViewStarDisplay"].Enabled = ms["RCMViewEDSM"].Enabled = rightclickmenu.Tag is ISystem || rightclickmenu.Tag is HistoryEntry;
                };
            }

            // Matrix calc holding transform info

            matrixcalc = new GLMatrixCalc();
            matrixcalc.PerspectiveNearZDistance = 1f;
            matrixcalc.PerspectiveFarZDistance = 120000f / lyscale;
            matrixcalc.InPerspectiveMode = true;
            matrixcalc.ResizeViewPort(this, glwfc.Size);          // must establish size before starting

            // menu system

            displaycontrol = new GLControlDisplay(items, glwfc, matrixcalc, true, 0.00001f, 0.00001f);       // hook form to the window - its the master
            displaycontrol.Font = new Font("Arial", 10f);
            displaycontrol.Focusable = true;          // we want to be able to focus and receive key presses.
            displaycontrol.SetFocus();

            // 3d controller

            gl3dcontroller = new Controller3D();
            gl3dcontroller.PosCamera.ZoomMax = 600;     // gives 5ly
            gl3dcontroller.ZoomDistance = 3000F / lyscale;
            gl3dcontroller.PosCamera.ZoomMin = 0.1f;
            gl3dcontroller.PosCamera.ZoomScaling = 1.1f;
            gl3dcontroller.YHoldMovement = true;
            gl3dcontroller.PaintObjects = Controller3DDraw;
            gl3dcontroller.KeyboardTravelSpeed = (ms, eyedist) =>
            {
                double eyedistr = Math.Pow(eyedist, 1.0);
                float v = (float)Math.Max(eyedistr / 2000, 0);
                //System.Diagnostics.Debug.WriteLine("Speed " + eyedistr + " "+ v);
                return (float)ms * v;
            };

            // hook gl3dcontroller to display control - its the slave. Do not register mouse UI, we will deal with that and feed any events thru we want
            gl3dcontroller.Start(matrixcalc, displaycontrol, new Vector3(0, 0, 0), new Vector3(140.75f, 0, 0), 0.5F, registermouseui:false, registerkeyui:true);

            if (displaycontrol != null)
            {
                displaycontrol.Paint += (o, ts) =>        // subscribing after start means we paint over the scene, letting transparency work
                {
                    // MCUB set up by Controller3DDraw which did the work first
                    galaxymenu.UpdateCoords(gl3dcontroller);
                    displaycontrol.Animate(glwfc.ElapsedTimems);
                    displaycontrol.Render(glwfc.RenderState, ts);
                };
            }

            displaycontrol.MouseClick += MouseClickOnMap;       // grab mouse UI
            displaycontrol.MouseUp += MouseUpOnMap;
            displaycontrol.MouseDown += MouseDownOnMap;
            displaycontrol.MouseMove += MouseMoveOnMap;
            displaycontrol.MouseWheel += MouseWheelOnMap;

            galaxymenu = new MapMenu(this,parts);

            // Autocomplete text box at top for searching

            GLTextBoxAutoComplete tbac = galaxymenu.EntryTextBox;
            if (tbac != null)
            {
                tbac.PerformAutoCompleteInUIThread = (s, a) =>
                {
                    System.Diagnostics.Debug.Assert(Application.MessageLoop);       // must be in UI thread
                    var glist = edsmmapping.galacticMapObjects.Where(x => s.Length < 3 ? x.name.StartsWith(s, StringComparison.InvariantCultureIgnoreCase) : x.name.Contains(s, StringComparison.InvariantCultureIgnoreCase)).Select(x => x).ToList();
                    List<string> list = glist.Select(x => x.name).ToList();
                    list.AddRange(travelpath.CurrentListSystem.Where(x => s.Length < 3 ? x.Name.StartsWith(s, StringComparison.InvariantCultureIgnoreCase) : x.Name.Contains(s, StringComparison.InvariantCultureIgnoreCase)).Select(x => x.Name));
                    list.Sort();
                    return list;
                };

                tbac.SelectedEntry = (a) =>     // in UI thread
                {
                    System.Diagnostics.Debug.Assert(Application.MessageLoop);       // must be in UI thread
                    System.Diagnostics.Debug.WriteLine("Selected " + tbac.Text);
                    var gmo = edsmmapping.galacticMapObjects.Find(x => x.name.Equals(tbac.Text, StringComparison.InvariantCultureIgnoreCase));
                    if (gmo != null)
                    {
                        System.Diagnostics.Debug.WriteLine("Move to gmo " + gmo.points[0]);
                        gl3dcontroller.SlewToPosition(new Vector3((float)gmo.points[0].X, (float)gmo.points[0].Y, (float)gmo.points[0].Z), -1);
                    }
                    else
                    {
                        var isys = travelpath.CurrentListSystem.Find(x => x.Name.Equals(tbac.Text, StringComparison.InvariantCultureIgnoreCase));
                        if (isys != null)
                        {
                            System.Diagnostics.Debug.WriteLine("Move to sys " + isys.Name);
                            gl3dcontroller.SlewToPosition(new Vector3((float)isys.X, (float)isys.Y, (float)isys.Z), -1);
                        }
                        else
                            tbac.InErrorCondition = true;
                    }
                };
            }

            if (galaxystars != null)
                galaxystars.Start();
        }

        public void UpdateNewHistoryEntry(HistoryList hl )   // new history entry
        {
            if (travelpath != null)
            {
                travelpath.CreatePath(hl);
                travelpath.SetSystem(hl.LastSystem);
            }
        }

        public void UpdateHistory(HistoryList hl)       // full history refresh
        {
            UpdateNewHistoryEntry(hl);
            UpdateLocalArea(hl);
        }
        
        public void UpdateLocalArea(HistoryList hl)
        {
            HistoryEntry he = hl.GetLast;       // may be null
            if (he != null && he.System.HasCoordinate && (parts & Parts.PrepopulateEDSMLocalArea) != 0)
            {
                galaxystars.Request9x3Box(new Vector3((float)he.System.X, (float)he.System.Y, (float)he.System.Z));
            }
        }
        #endregion

        #region Display

        // Context is set.
        public void Systick()
        {
            gl3dcontroller.HandleKeyboardSlews(true, OtherKeys);
            gl3dcontroller.RecalcMatrixIfMoved();
            glwfc.Invalidate();
        }

        float lasteyedistance = 100000000;
        int lastgridwidth = 100;

        // Context is set.
        private void Controller3DDraw(Controller3D c3d, ulong time)
        {
            GLMatrixCalcUniformBlock mcb = ((GLMatrixCalcUniformBlock)items.UB("MCUB"));
            mcb.SetText(gl3dcontroller.MatrixCalc);        // set the matrix unform block to the controller 3d matrix calc.

            // set up the grid shader size

            if (gridshader != null && gridshader.Enable)
            {
                // set the dynamic grid properties

                var vertshader = gridshader.GetShader<DynamicGridVertexShader>(ShaderType.VertexShader);
                //remove for now if (Math.Abs(lasteyedistance - gl3dcontroller.MatrixCalc.EyeDistance) > 10)     // a little histerisis, set the vertical shader grid size
                {
                    gridrenderable.InstanceCount = vertshader.ComputeGridSize(gl3dcontroller.MatrixCalc.LookAt.Y, gl3dcontroller.MatrixCalc.EyeDistance, out lastgridwidth);
                    lasteyedistance = gl3dcontroller.MatrixCalc.EyeDistance;
                }

                vertshader.SetUniforms(gl3dcontroller.MatrixCalc.LookAt, lastgridwidth, gridrenderable.InstanceCount);
    
                // set the coords fader

                float coordfade = lastgridwidth == 10000 ? (0.7f - (c3d.MatrixCalc.EyeDistance / 20000).Clamp(0.0f, 0.7f)) : 0.7f;
                Color coordscol = Color.FromArgb(coordfade < 0.05 ? 0 : 150, Color.Cyan);
                var tvshader = gridtextshader.GetShader<DynamicGridCoordVertexShader>(ShaderType.VertexShader);
                tvshader.ComputeUniforms(lastgridwidth, gl3dcontroller.MatrixCalc, gl3dcontroller.PosCamera.CameraDirection, coordscol, Color.Transparent);
            }

            //// set the galaxy volumetric block

            if (galaxyrenderable != null && galaxyshader.Enable)
            {
                galaxyrenderable.InstanceCount = volumetricblock.Set(gl3dcontroller.MatrixCalc, volumetricboundingbox, gl3dcontroller.MatrixCalc.InPerspectiveMode ? 50.0f : 0);        // set up the volumentric uniform
                //System.Diagnostics.Debug.WriteLine("GI {0}", galaxyrendererable.InstanceCount);
                galaxyshader.SetDistance(gl3dcontroller.MatrixCalc.InPerspectiveMode ? c3d.MatrixCalc.EyeDistance : -1f);
            }

            if (travelpath != null)
                travelpath.Update(time, gl3dcontroller.MatrixCalc.EyeDistance);

            if (galmapobjects != null && galmapobjects.Enable)
                galmapobjects.Update(time, gl3dcontroller.MatrixCalc.EyeDistance);

            if (galaxystars != null && galaxystars.Enable)
            {
                if ( (parts & Parts.AllowAutoEDSMStarsUpdate) != 0 && gl3dcontroller.MatrixCalc.EyeDistance < 400)         // if auto update, and eyedistance close, see if local stars needed
                    galaxystars.Request9x3BoxConditional(gl3dcontroller.PosCamera.LookAt);

                galaxystars.Update(time, gl3dcontroller.MatrixCalc.EyeDistance);
            }

            rObjects.Render(glwfc.RenderState, gl3dcontroller.MatrixCalc, verbose: false);

            GL.Flush(); // ensure everything is in the grapghics pipeline
        }

        #endregion

#region Turn on/off, move, etc.

        public bool GalaxyDisplay { get { return galaxyshader?.Enable ?? false; } set { if ( galaxyshader!=null) galaxyshader.Enable = value; glwfc.Invalidate(); } }
        public bool StarDotsSpritesDisplay { get { return stardots?.Enable ?? false; } set { if ( stardots!=null) stardots.Enable = starsprites.Enable = value; glwfc.Invalidate(); } }
        public bool GalaxyStars { get { return galaxystars?.Enable ?? false; } set { if (galaxystars != null)  galaxystars.Enable = value; glwfc.Invalidate(); } }
        public int GalaxyStarsMaxObjects { get { return galaxystars?.MaxObjectsAllowed ?? 100000; } set { if ( galaxystars != null && value >= 10000) galaxystars.MaxObjectsAllowed = value; } }  // screen out stupid numbers (may have got itself saved with that due to debugging)
        public bool Grid { get { return gridshader?.Enable ?? false; } set { if (gridshader != null)  gridshader.Enable = gridtextshader.Enable = value; glwfc.Invalidate(); } }

        public bool TravelPathTapeDisplay { get { return travelpath?.EnableTape ?? false; } set { if (travelpath != null) travelpath.EnableTape = value; glwfc.Invalidate(); } }
        public void TravelPathRefresh() { if ( travelpath != null ) travelpath.Refresh(); }   // travelpath.Refresh() manually after these have changed
        public DateTime TravelPathStartDate { get { return travelpath?.TravelPathStartDate ?? DateTime.MinValue; } set { if (travelpath != null && travelpath.TravelPathStartDate != value) { travelpath.TravelPathStartDate = value; } } }
        public bool TravelPathStartDateEnable { get { return travelpath?.TravelPathStartDateEnable ?? false; } set { if (travelpath != null && travelpath.TravelPathStartDateEnable != value) { travelpath.TravelPathStartDateEnable = value; } } }
        public DateTime TravelPathEndDate { get { return travelpath?.TravelPathEndDate ?? DateTime.MinValue; } set { if (travelpath != null && travelpath.TravelPathEndDate != value) { travelpath.TravelPathEndDate = value; } } }
        public bool TravelPathEndDateEnable { get { return travelpath?.TravelPathEndDateEnable ?? false; } set { if (travelpath != null && travelpath.TravelPathEndDateEnable != value) { travelpath.TravelPathEndDateEnable = value; } } }

        public bool GalObjectDisplay { get { return galmapobjects?.Enable ?? false; } set { if (galmapobjects!=null) galmapobjects.Enable = value; glwfc.Invalidate(); } }
        public void SetGalObjectTypeEnable(string id, bool state) { if (galmapobjects != null) galmapobjects.SetGalObjectTypeEnable(id, state); glwfc.Invalidate(); }
        public bool GetGalObjectTypeEnable(string id) { return galmapobjects?.GetGalObjectTypeEnable(id) ?? false; }
        public void SetAllGalObjectTypeEnables(string set) { if (galmapobjects != null) galmapobjects.SetAllEnables(set); glwfc.Invalidate(); }
        public string GetAllGalObjectTypeEnables() { return galmapobjects?.GetAllEnables() ?? ""; }
        public bool EDSMRegionsEnable { get { return edsmgalmapregions?.Enable ?? false; } set { if (edsmgalmapregions != null) edsmgalmapregions .Enable = value; glwfc.Invalidate(); } }
        public bool EDSMRegionsOutlineEnable { get { return edsmgalmapregions?.Outlines ?? false; } set { if (edsmgalmapregions != null) edsmgalmapregions.Outlines = value; glwfc.Invalidate(); } }
        public bool EDSMRegionsShadingEnable { get { return edsmgalmapregions?.Regions ?? false; } set { if ( edsmgalmapregions != null ) edsmgalmapregions.Regions = value; glwfc.Invalidate(); } }
        public bool EDSMRegionsTextEnable { get { return edsmgalmapregions?.Text ?? false; } set { if (edsmgalmapregions != null) edsmgalmapregions.Text = value; glwfc.Invalidate(); } }
        public bool EliteRegionsEnable { get { return elitemapregions?.Enable ?? false; } set { if (elitemapregions != null) elitemapregions.Enable = value; glwfc.Invalidate(); } }
        public bool EliteRegionsOutlineEnable { get { return elitemapregions?.Outlines ?? false; } set { if (elitemapregions != null) elitemapregions.Outlines = value; glwfc.Invalidate(); } }
        public bool EliteRegionsShadingEnable { get { return elitemapregions?.Regions ?? false; } set { if (elitemapregions != null) elitemapregions.Regions = value; glwfc.Invalidate(); } }
        public bool EliteRegionsTextEnable { get { return elitemapregions?.Text ?? false; } set { if (elitemapregions != null) elitemapregions.Text = value; glwfc.Invalidate(); } }

        public void GoToTravelSystem(int dir)      //0 = home, 1 = next, -1 = prev
        {
            var isys = dir == 0 ? travelpath.CurrentSystem : (dir < 0 ? travelpath.PrevSystem() : travelpath.NextSystem());
            if (isys != null)
            {
                gl3dcontroller.SlewToPosition(new Vector3((float)isys.X, (float)isys.Y, (float)isys.Z), -1);
                SetEntryText(isys.Name);
            }
        }

        public void GoToSystem(ISystem isys, float lydist = 50f)
        {
            gl3dcontroller.SlewToPositionZoom(new Vector3((float)isys.X, (float)isys.Y, (float)isys.Z), gl3dcontroller.ZoomDistance / lydist, -1);
            SetEntryText(isys.Name);
        }

        public void ViewGalaxy()
        {
            gl3dcontroller.PosCamera.CameraRotation = 0;
            gl3dcontroller.PosCamera.GoToZoomPan(new Vector3(0, 0, 0), new Vector2(140.75f, 0), 0.5f,3);
        }

        #endregion

        #region Helpers

        public void LoadState(MapSaver defaults, bool restorepos)
        {
            GalaxyDisplay = defaults.GetSetting("GD", true);
            StarDotsSpritesDisplay = defaults.GetSetting("SDD", true);

            TravelPathTapeDisplay = defaults.GetSetting("TPD", true);
            TravelPathStartDate = defaults.GetSetting("TPSD", new DateTime(2014, 12, 16));
            TravelPathStartDateEnable = defaults.GetSetting("TPSDE", false);
            TravelPathEndDate = defaults.GetSetting("TPED", DateTime.UtcNow.AddMonths(1));
            TravelPathEndDateEnable = defaults.GetSetting("TPEDE", false);
            if (TravelPathStartDateEnable || TravelPathEndDateEnable)
                travelpath.Refresh();       // and refresh it if we set the data

            GalObjectDisplay = defaults.GetSetting("GALOD", true);
            SetAllGalObjectTypeEnables(defaults.GetSetting("GALOBJLIST", ""));

            EDSMRegionsEnable = defaults.GetSetting("ERe", false);
            EDSMRegionsOutlineEnable = defaults.GetSetting("ERoe", false);
            EDSMRegionsShadingEnable = defaults.GetSetting("ERse", false);
            EDSMRegionsTextEnable = defaults.GetSetting("ERte", false);

            EliteRegionsEnable = defaults.GetSetting("ELe", true);
            EliteRegionsOutlineEnable = defaults.GetSetting("ELoe", true);
            EliteRegionsShadingEnable = defaults.GetSetting("ELse", false);
            EliteRegionsTextEnable = defaults.GetSetting("ELte", true);

            Grid = defaults.GetSetting("GRIDS", true);
            GalaxyStars = defaults.GetSetting("GALSTARS", true);
            GalaxyStarsMaxObjects = defaults.GetSetting("GALSTARSOBJ", 500000);

            if (restorepos )
                gl3dcontroller.SetPositionCamera(defaults.GetSetting("POSCAMERA", ""));     // go thru gl3dcontroller to set default position, so we reset the model matrix
        }

        public void SaveState(MapSaver defaults)
        {
            defaults.PutSetting("GD", GalaxyDisplay);
            defaults.PutSetting("SDD", StarDotsSpritesDisplay);
            defaults.PutSetting("TPD", TravelPathTapeDisplay);
            defaults.PutSetting("TPSD", TravelPathStartDate);
            defaults.PutSetting("TPSDE", TravelPathStartDateEnable);
            defaults.PutSetting("TPED", TravelPathEndDate);
            defaults.PutSetting("TPEDE", TravelPathEndDateEnable);
            defaults.PutSetting("GALOD", GalObjectDisplay);
            defaults.PutSetting("GALOBJLIST", GetAllGalObjectTypeEnables());
            defaults.PutSetting("ERe", EDSMRegionsEnable);
            defaults.PutSetting("ERoe", EDSMRegionsOutlineEnable);
            defaults.PutSetting("ERse", EDSMRegionsShadingEnable);
            defaults.PutSetting("ERte", EDSMRegionsTextEnable);
            defaults.PutSetting("ELe", EliteRegionsEnable);
            defaults.PutSetting("ELoe", EliteRegionsOutlineEnable);
            defaults.PutSetting("ELse", EliteRegionsShadingEnable);
            defaults.PutSetting("ELte", EliteRegionsTextEnable);
            defaults.PutSetting("GRIDS", Grid);
            defaults.PutSetting("GALSTARS", GalaxyStars);
            defaults.PutSetting("GALSTARSOBJ", GalaxyStarsMaxObjects);
            defaults.PutSetting("POSCAMERA", gl3dcontroller.PosCamera.StringPositionCamera);
        }

        private void SetEntryText(string text)
        {
            if (galaxymenu.EntryTextBox != null)
            {
                galaxymenu.EntryTextBox.Text = text;
                galaxymenu.EntryTextBox.CancelAutoComplete();
            }
            displaycontrol.SetFocus();
        }

        private Object FindObjectOnMap(Point loc)
        {
            float tz = float.MinValue, gz = float.MinValue, sz = float.MinValue;
            var he = travelpath?.FindSystem(loc, glwfc.RenderState, matrixcalc.ViewPort.Size, out tz);     //z are maxvalue if not found, will return an HE since travelpath is made with it
            var gmo = galmapobjects?.FindPOI(loc, glwfc.RenderState, matrixcalc.ViewPort.Size, out gz);
            var sys = galaxystars?.Find(loc, glwfc.RenderState, matrixcalc.ViewPort.Size, out sz);

            if (gmo != null && gz < sz && gz < tz)      // got gmo, and closer than the other two
                return gmo;
            if (he != null )                            // he is prefered over sys since it has more data associated with it
                return he;
            if (sys != null)
            {
                ISystem s = EliteDangerousCore.DB.SystemCache.FindSystem(sys.Name); // look up by name
                if (s != null)                          // must normally be found, so return
                    return s;
            }
            return null;
        }

        // from obj, return info about it, its name, location, and description
        private Tuple<string, Vector3, string> NameLocationDescription(Object obj)       
        {
            var he = obj as EliteDangerousCore.HistoryEntry;
            var gmo = obj as GalacticMapObject;
            var sys = obj as ISystem;
            if (he != null)
            {
                string info = $"Visited on:";

                foreach ( var e in travelpath.CurrentListHE)
                {
                    if ( e.System.Name.Equals(he.System.Name))
                    {
                        var t = EDDiscoveryForm.EDDConfig.ConvertTimeToSelectedFromUTC(e.EventTimeUTC);
                        info = info.AppendPrePad($"@" + t.ToString(), Environment.NewLine);
                    }
                }

                return new Tuple<string, Vector3, string>(he.System.Name,
                                                          new Vector3((float)he.System.X, (float)he.System.Y, (float)he.System.Z),
                                                          info );
            }
            else if (gmo != null)
            {
                return new Tuple<string, Vector3, string>(gmo.name,
                                                          new Vector3((float)gmo.points[0].X, (float)gmo.points[0].Y, (float)gmo.points[0].Z),
                                                          gmo.description);
            }
            else if (sys != null)
            {
                return new Tuple<string, Vector3, string>(sys.Name,
                                                            new Vector3((float)sys.X, (float)sys.Y, (float)sys.Z), 
                                                            $"EDSM Star");
            }
            else
            {
                return null;
            }
        }


#endregion

#region UI

        private void MouseDownOnMap(Object s, GLMouseEventArgs e)
        {
            gl3dcontroller.MouseDown(s, e);
        }

        private void MouseUpOnMap(Object s, GLMouseEventArgs e)
        {
            gl3dcontroller.MouseUp(s, e);
        }

        private void MouseMoveOnMap(Object s, GLMouseEventArgs e)
        {
            gl3dcontroller.MouseMove(s, e);
        }

        private void MouseClickOnMap(Object s, GLMouseEventArgs e)
        {
            int distmovedsq = gl3dcontroller.MouseMovedSq(e.WindowLocation);        //3dcontroller is monitoring mouse movements
            if (distmovedsq < 4)
            {
                //  System.Diagnostics.Debug.WriteLine("map click");
                Object item = FindObjectOnMap(e.ViewportLocation);

                if (item != null)
                {
                    if (e.Button == GLMouseEventArgs.MouseButtons.Left)
                    {
                        if (item is HistoryEntry)
                            travelpath.SetSystem((item as HistoryEntry).System);
                        var nl = NameLocationDescription(item);
                        System.Diagnostics.Debug.WriteLine("Click on and slew to " + nl.Item1);
                        SetEntryText(nl.Item1);
                        gl3dcontroller.SlewToPosition(nl.Item2, -1);
                    }
                    else if (e.Button == GLMouseEventArgs.MouseButtons.Right)
                    {
                        if (rightclickmenu != null)
                        {
                            rightclickmenu.Tag = item;
                            rightclickmenu.Show(displaycontrol, e.Location);
                        }
                    }
                }
            }
        }

        private void MouseWheelOnMap(Object s, GLMouseEventArgs e)
        {
            gl3dcontroller.MouseWheel(s, e);
        }

        private void OtherKeys(GLOFC.Controller.KeyboardMonitor kb)
        {
            // See OFC PositionCamera for keys used
            // F keys are reserved for KeyPresses action pack

            if ((parts & Parts.PerspectiveChange) != 0 && kb.HasBeenPressed(Keys.P, GLOFC.Controller.KeyboardMonitor.ShiftState.None))
            {
                gl3dcontroller.ChangePerspectiveMode(!gl3dcontroller.MatrixCalc.InPerspectiveMode);
            }

            if ((parts & Parts.YHoldButton) != 0 && kb.HasBeenPressed(Keys.O, KeyboardMonitor.ShiftState.None))
            {
                gl3dcontroller.PosCamera.YHoldMovement = !gl3dcontroller.PosCamera.YHoldMovement;
            }

            if (kb.HasBeenPressed(Keys.D1, GLOFC.Controller.KeyboardMonitor.ShiftState.None))
            {
                GalaxyDisplay = !GalaxyDisplay;
            }
            if (kb.HasBeenPressed(Keys.D2, GLOFC.Controller.KeyboardMonitor.ShiftState.None))
            {
                Grid = !Grid;
            }
            if (kb.HasBeenPressed(Keys.D3, GLOFC.Controller.KeyboardMonitor.ShiftState.None))
            {
                StarDotsSpritesDisplay = !StarDotsSpritesDisplay;
            }
            if (kb.HasBeenPressed(Keys.D4, GLOFC.Controller.KeyboardMonitor.ShiftState.None))
            {
                TravelPathTapeDisplay = !TravelPathTapeDisplay;
            }
            if (kb.HasBeenPressed(Keys.D5, GLOFC.Controller.KeyboardMonitor.ShiftState.None))
            {
                GalObjectDisplay = !GalObjectDisplay;
            }
            if (kb.HasBeenPressed(Keys.D6, GLOFC.Controller.KeyboardMonitor.ShiftState.None))
            {
                GalaxyStars = !GalaxyStars;
            }
            if (kb.HasBeenPressed(Keys.D7, GLOFC.Controller.KeyboardMonitor.ShiftState.None))
            {
                if (EDSMRegionsEnable)
                    EDSMRegionsOutlineEnable = !EDSMRegionsOutlineEnable;
                else
                    EliteRegionsOutlineEnable = !EliteRegionsOutlineEnable;
            }
            if (kb.HasBeenPressed(Keys.D8, GLOFC.Controller.KeyboardMonitor.ShiftState.None))
            {
                if (EDSMRegionsEnable)
                    EDSMRegionsShadingEnable = !EDSMRegionsShadingEnable;
                else
                    EliteRegionsShadingEnable = !EliteRegionsShadingEnable;
            }
            if (kb.HasBeenPressed(Keys.D9, GLOFC.Controller.KeyboardMonitor.ShiftState.None))
            {
                if (EDSMRegionsEnable)
                    EDSMRegionsTextEnable = !EDSMRegionsTextEnable;
                else
                    EliteRegionsTextEnable = !EliteRegionsTextEnable;
            }
        }

        #endregion


    }
}