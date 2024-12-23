/*
 * Copyright © 2022 - 2023 EDDiscovery development team
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
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace EDDiscovery.UserControls
{
    public partial class UserControlEngineers : UserControlCommonBase
    {
        private List<EngineerStatusPanel> engineerpanels;
        private bool isHistoric = false;
        private HistoryEntry last_he = null;
        RecipeFilterSelector efs;

        private string dbHistoricMatsSave = "HistoricMaterials";
        private string dbWordWrap = "WordWrap";
        private string dbEngFilterSave = "EngineerFilter";
        private string dbWSave = "Wanted";
        private string dbMoreInfo = "MoreInfo";

        public UserControlEngineers()
        {
            InitializeComponent();
            DBBaseName = "Engineers";
        }

        public override void Init()
        {
            isHistoric = GetSetting(dbHistoricMatsSave, false);
            chkNotHistoric.Checked = !isHistoric;
            this.chkNotHistoric.CheckedChanged += new System.EventHandler(this.chkNotHistoric_CheckedChanged);

            extCheckBoxWordWrap.Checked = GetSetting(dbWordWrap, false);
            extCheckBoxWordWrap.Click += extCheckBoxWordWrap_Click;     // install after setup
            extCheckBoxMoreInfo.Checked = GetSetting(dbMoreInfo, false);
            extCheckBoxMoreInfo.Click += extCheckBoxMoreInfo_Click;

            List<string> engineers = Recipes.EngineeringRecipes.SelectMany(r => r.Engineers).Distinct().ToList();
            engineers.Sort();
            efs = new RecipeFilterSelector(engineers);
            efs.UC.AddGroupItem(string.Join(";", ItemData.ShipEngineers()), "Ship Engineers");
            efs.UC.AddGroupItem(string.Join(";", ItemData.OnFootEngineers()), "On Foot Engineers");
            efs.UC.AddGroupItem("Guardian;Guardian Weapons;Human;Special Effect;Suit;Weapon;", "Other Enginnering");
            efs.SaveSettings += (newvalue, e) =>
            {
                string prevsetting = GetSetting(dbEngFilterSave, "All");
                if (prevsetting != newvalue)
                {
                    PutSetting(dbEngFilterSave, newvalue);
                    SetupDisplay();
                }
            };

            var enumlisttt = new Enum[] { EDTx.UserControlEngineers_buttonFilterEngineer_ToolTip,
                        EDTx.UserControlEngineers_extCheckBoxWordWrap_ToolTip, EDTx.UserControlEngineers_extCheckBoxMoreInfo_ToolTip,
                        EDTx.UserControlEngineers_buttonClear_ToolTip, EDTx.UserControlEngineers_extButtonPushResources_ToolTip,
                        EDTx.UserControlEngineers_chkNotHistoric_ToolTip};
            BaseUtils.Translator.Instance.TranslateTooltip(toolTip, enumlisttt, this);

            DiscoveryForm.OnNewEntry += Discoveryform_OnNewEntry;
            DiscoveryForm.OnHistoryChange += RefreshData;


        }

        public override void Closing()
        {
            DiscoveryForm.OnNewEntry -= Discoveryform_OnNewEntry;
            DiscoveryForm.OnHistoryChange -= RefreshData;

            PutSetting(dbHistoricMatsSave, isHistoric);
        }

        public override void InitialDisplay()
        {
            SetupDisplay();
            RefreshData();
        }

        private void RefreshData()
        {
            if (isHistoric)
            {
                RequestPanelOperation(this, new UserControlCommonBase.RequestTravelHistoryPos());
            }
            else
            {
                last_he = DiscoveryForm.History.GetLast;
                UpdateDisplay();
            }
        }

        private void Discoveryform_OnNewEntry(HistoryEntry he)
        {
            if (!isHistoric)        // only track new items if not historic
            {
                last_he = he;
                if (he.journalEntry is ICommodityJournalEntry || he.journalEntry is IMaterialJournalEntry)
                {
                    UpdateDisplay();
                }
            }
        }

        public override void ReceiveHistoryEntry(HistoryEntry he)
        {
            if (isHistoric)
            {
                last_he = he;
                UpdateDisplay();
            }
        }


        private int PanelHeight; // Height of each engineer panel
        private List<string> engineers; // List of engineers
        private int totalVisiblePanels; // Number of panels visible on screen
        private Dictionary<int, EngineerStatusPanel> visiblePanels = new Dictionary<int, EngineerStatusPanel>();


        public void SetupDisplay()
        {
            // Suspend layout to optimize UI updates
            panelEngineers.SuspendLayout();

            // Clean up previous panels
            if (visiblePanels != null)
            {
                foreach (var panel in visiblePanels.Values)
                {
                    panel.UnInstallEvents();
                    panelEngineers.Controls.Remove(panel);
                }
                visiblePanels.Clear();
            }

            // Apply filter and update the list
            string filter = GetSetting(dbEngFilterSave, "All");
            engineers = Recipes.EngineeringRecipes
                .SelectMany(r => r.Engineers)
                .Distinct()
                .Where(e => filter.Equals("All", StringComparison.InvariantCultureIgnoreCase) || filter.Contains(e, StringComparison.InvariantCultureIgnoreCase))
                .OrderBy(e => e)
                .ToList();

            // Initialize panel height
            InitializePanelHeight();

            // Add placeholders for each panel
            for (int i = 0; i < engineers.Count; i++)
            {
                var placeholder = CreatePlaceholderPanel();
                placeholder.Bounds = new Rectangle(0, i * PanelHeight, panelEngineers.Width - panelEngineers.ScrollBarWidth - 4, PanelHeight);
                visiblePanels[i] = placeholder;
                panelEngineers.Controls.Add(placeholder);
            }

            CalculateVisiblePanelCount();
            UpdateScrollRange();
            UpdateVisiblePanels(0);

            // Attach scroll event
            panelEngineers.ScrollBar.ValueChanged -= ScrollBar_ValueChanged;
            panelEngineers.ScrollBar.ValueChanged += ScrollBar_ValueChanged;

            // Resume layout after updates
            panelEngineers.ResumeLayout();
        }


        private void InitializePanelHeight()
        {
            if (PanelHeight == 0) // Calculate only if not already set
            {
                var tempPanel = new EngineerStatusPanel();
                PanelHeight = tempPanel.GetVSize(extCheckBoxMoreInfo.Checked); // Dynamic height or fallback
            }
        }


        private void CalculateVisiblePanelCount()
        {
            if (PanelHeight == 0) return; // Ensure PanelHeight is set
            totalVisiblePanels = Math.Max(1, panelEngineers.Height / PanelHeight); // At least 1 panel visible
        }

        private void UpdateVisiblePanels(int scrollPosition)
        {
            if (engineers == null || engineers.Count == 0)
                return;

            // Determine visible range with buffer
            int firstVisibleIndex = Math.Max(0, scrollPosition / PanelHeight - 1); // -1 for buffer
            int lastVisibleIndex = Math.Min(firstVisibleIndex + totalVisiblePanels + 2, engineers.Count - 1); // +2 for buffer

            // Replace placeholders in the visible range
            for (int i = firstVisibleIndex; i <= lastVisibleIndex; i++)
            {
                if (visiblePanels.ContainsKey(i))
                {
                    var panel = visiblePanels[i];
                    if (!panel.Enabled) // Replace placeholders
                    {
                        var realPanel = CreateEngineerPanel(engineers[i]);
                        realPanel.Bounds = panel.Bounds;

                        visiblePanels[i] = realPanel;

                        panelEngineers.Controls.Remove(panel);
                        panelEngineers.Controls.Add(realPanel);

                        UpdatePanelStatus(realPanel);
                    }
                }
            }

            panelEngineers.PerformLayout();
        }


        private void UpdatePanelStatus(EngineerStatusPanel panel)
        {
            var lastengprog = DiscoveryForm.History.GetLastHistoryEntry(x => x.EntryType == JournalTypeEnum.EngineerProgress, last_he);
            var system = last_he?.System;
            var mcllist = last_he != null ? DiscoveryForm.History.MaterialCommoditiesMicroResources.Get(last_he.MaterialCommodity) : null;

            List<HistoryEntry> crafts = null;
            if (last_he != null)
            {
                crafts = DiscoveryForm.History.Engineering.Get(last_he.Engineering, EngineerCrafting.TechBrokerID);
            }

            string status = "";
            if (lastengprog != null && panel.EngineerInfo != null)
            {
                var state = (lastengprog.journalEntry as EliteDangerousCore.JournalEvents.JournalEngineerProgress)?.Progress(panel.Name);
                if (state == EliteDangerousCore.JournalEvents.JournalEngineerProgress.InviteState.UnknownEngineer)
                    state = EliteDangerousCore.JournalEvents.JournalEngineerProgress.InviteState.None;
                status = state.ToString();
            }

            panel.UpdateStatus(status, system, mcllist, crafts);
        }

        private void ScrollBar_ValueChanged(object sender, EventArgs e)
        {
            int maxScroll = panelEngineers.VerticalScroll.Maximum;
            int minScroll = panelEngineers.VerticalScroll.Minimum;

            if (panelEngineers.VerticalScroll.Value < minScroll || panelEngineers.VerticalScroll.Value > maxScroll)
            {
                panelEngineers.VerticalScroll.Value = Math.Max(minScroll, Math.Min(maxScroll, panelEngineers.VerticalScroll.Value));
            }

            UpdateVisiblePanels(panelEngineers.ScrollBar.Value);
        }

        private void UpdateScrollRange()
        {
            if (engineers == null || engineers.Count == 0)
            {
                panelEngineers.VerticalScroll.Maximum = 0;
                return;
            }

            int totalHeight = engineers.Count * PanelHeight;

            panelEngineers.VerticalScroll.Maximum = Math.Max(0, totalHeight - panelEngineers.Height);
            panelEngineers.VerticalScroll.LargeChange = panelEngineers.Height;
            panelEngineers.VerticalScroll.SmallChange = Math.Max(10, PanelHeight / 2);
        }

        private EngineerStatusPanel CreatePlaceholderPanel()
        {
            var placeholder = new EngineerStatusPanel
            {
                BackColor = Color.Black,
                Enabled = false,
                Visible = true
            };

            placeholder.Size = new Size(panelEngineers.Width - panelEngineers.ScrollBarWidth - 4, PanelHeight);

            return placeholder;
        }

        private EngineerStatusPanel CreateEngineerPanel(string name)
        {
            var panel = new EngineerStatusPanel();
            var info = ItemData.GetEngineerInfo(name);

            panel.Init(name, info, GetSetting(dbWSave + "_" + name, ""), DGVSaveName());
            panel.UpdateWordWrap(extCheckBoxWordWrap.Checked);

            panel.SaveSettings += () => PutSetting(dbWSave + "_" + name, panel.WantedPerRecipe.ToString(","));
            panel.AskForRedisplay += UpdateDisplay;

            panel.ColumnSetupChanged += (changedPanel) =>
            {
                changedPanel.SaveDGV(DGVSaveName());
                foreach (var otherPanel in visiblePanels.Values.Where(p => p != changedPanel))
                {
                    otherPanel.LoadDGV(DGVSaveName());
                }
            };
            return panel;
        }



        public void UpdateDisplay()
        {
            var lastengprog = DiscoveryForm.History.GetLastHistoryEntry(x => x.EntryType == JournalTypeEnum.EngineerProgress, last_he);
            var system = last_he?.System;
            var mcllist = last_he != null ? DiscoveryForm.History.MaterialCommoditiesMicroResources.Get(last_he.MaterialCommodity) : null;

            List<HistoryEntry> crafts = null;
            if (last_he != null)
            {
                crafts = DiscoveryForm.History.Engineering.Get(last_he.Engineering, EngineerCrafting.TechBrokerID);
            }

            foreach (var panel in visiblePanels.Values.Where(p => p.Visible))
            {
                string status = "";
                if (lastengprog != null && panel.EngineerInfo != null)
                {
                    var state = (lastengprog.journalEntry as EliteDangerousCore.JournalEvents.JournalEngineerProgress)?.Progress(panel.Name);
                    if (state == EliteDangerousCore.JournalEvents.JournalEngineerProgress.InviteState.UnknownEngineer)
                        state = EliteDangerousCore.JournalEvents.JournalEngineerProgress.InviteState.None;
                    status = state.ToString();
                }

                panel.UpdateStatus(status, system, mcllist, crafts);
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (engineerpanels != null)
            {
                for (int i = 0; i < engineerpanels.Count; i++)
                {
                    engineerpanels[i].Width = panelEngineers.Width - panelEngineers.ScrollBarWidth - 4;
                }
            }
            CalculateVisiblePanelCount();
            UpdateScrollRange();
        }

        #region UI

        private void buttonFilterEngineer_Click(object sender, EventArgs e)
        {
            Button b = sender as Button;
            efs.Open(GetSetting(dbEngFilterSave, "All"), b, this.FindForm());
        }

        private void extCheckBoxWordWrap_Click(object sender, EventArgs e)
        {
            PutSetting(dbWordWrap, extCheckBoxWordWrap.Checked);
            foreach (var p in engineerpanels.DefaultIfEmpty())
                p.UpdateWordWrap(extCheckBoxWordWrap.Checked);
        }

        private void extCheckBoxMoreInfo_Click(object sender, EventArgs e)
        {
            PutSetting(dbMoreInfo, extCheckBoxMoreInfo.Checked);
            int vpos = 0;

            panelEngineers.SuspendLayout();

            for (int i = 0; i < engineerpanels.Count; i++)
            {
                var ep = engineerpanels[i];
                int panelvspacing = ep.GetVSize(extCheckBoxMoreInfo.Checked);

                // need to set bounds after adding, for some reason
                ep.Bounds = new Rectangle(0, vpos, panelEngineers.Width - panelEngineers.ScrollBarWidth - 4, panelvspacing);
                vpos += panelvspacing + 4;
            }
            panelEngineers.ResumeLayout();
        }

        private void chkNotHistoric_CheckedChanged(object sender, EventArgs e)
        {
            isHistoric = !chkNotHistoric.Checked;
            RefreshData();
        }

        private void extButtonPushResources_Click(object sender, EventArgs e)
        {
            Dictionary<MaterialCommodityMicroResourceType, int> resourcelist = new Dictionary<MaterialCommodityMicroResourceType, int>();
            foreach (var p in engineerpanels)
            {
                foreach (var kvp in p.NeededResources)
                {
                    if (resourcelist.TryGetValue(kvp.Key, out int value))
                    {
                        resourcelist[kvp.Key] = value + kvp.Value;
                    }
                    else
                        resourcelist[kvp.Key] = kvp.Value;
                }
            }

            var req = new UserControlCommonBase.PushResourceWantedList() { Resources = resourcelist };
            RequestPanelOperationOpen(PanelInformation.PanelIDs.Resources, req);
        }

        private void buttonClear_Click(object sender, EventArgs e)
        {
            foreach (var x in engineerpanels)
            {
                x.Clear();
            }

            UpdateDisplay();
        }

        #endregion

    }
}