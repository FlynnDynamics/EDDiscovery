﻿/*
 * Copyright 2025 - 2025 EDDiscovery development team
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

using System;
using System.Drawing;
using System.Windows.Forms;

namespace EDDiscovery.UserControls
{
    public partial class UserControlColonisation : UserControlCommonBase
    {
        public UserControlColonisation()
        {
            InitializeComponent();
        }

        public override void Init()
        {
        }

        public override void InitialDisplay()
        {
        }

        public override void Closing()
        {
        }

        public override bool SupportTransparency => true;
        public override void SetTransparency(bool on, Color curcol)
        {
            this.BackColor = curcol;
            extTabControl.PaintTransparentColor = on ? curcol : Color.Transparent;
        }
    }
}
