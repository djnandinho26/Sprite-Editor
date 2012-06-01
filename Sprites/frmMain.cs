﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SpriteEditor
{
    public partial class frmMain : Form
    {
        private static readonly Regex _regex = new Regex(@"\d+$");
        private string _request = "";
        private string _workingFile = "";
        private Image _imageOriginal;
        private List<string> _imageLocations;
        private List<Image> _images;
        private List<ToolStripMenuItem> _dependencies; 
        private readonly string[] _possibleParams = {
                                                        "uri", "flipX", "sizeMultiplier", "sizeDivider", "frameDelay",
                                                        "cropX", "cropY", "cropW", "cropH",
                                                        "offsX", "offsY", "isChain", "usePrevious", "autoClose",
                                                        "transparent", "walkMultiplier"
                                                    };
        private readonly string[] _possibleStates = {
                                                        "SPRITE_META_DATA", "SPRITE_STATE_DEFAULT",
                                                        "SPRITE_STATE_STAND_LEFT", "SPRITE_STATE_STAND_RIGHT",
                                                        "SPRITE_STATE_WALK_LEFT", "SPRITE_STATE_WALK_RIGHT",
                                                        "SPRITE_STATE_JUMP_LEFT", "SPRITE_STATE_JUMP_RIGHT",
                                                        "SPRITE_STATE_DESTROY_LEFT", "SPRITE_STATE_DESTROY_RIGHT",
                                                        "SPRITE_STATE_RUN_LEFT", "SPRITE_STATE_RUN_RIGHT",
                                                        "SPRITE_STATE_FLY_LEFT", "SPRITE_STATE_FLY_RIGHT"
                                                    };

        private readonly string[] _possibleFlags = {
                                                       "isCollector", "isItem", "disablePhysics",
                                                       "disableWindowCollide", "disableSpriteCollide",
                                                       "disableJump", "doFadeOut"
                                                   };

        private readonly string[] _possibleActions = {"walk", "fly", "run", "jump", "destroy", "death"};

        public frmMain() { InitializeComponent(); }

        // application start
        private void frmMain_Load(object sender, EventArgs e)
        {
            _images = new List<Image>();
            _imageLocations = new List<string>();
            _dependencies = new List<ToolStripMenuItem>();

            imageDisplay.Width = imageDisplay.Image.Width;
            imageDisplay.Height = imageDisplay.Image.Height;
            _imageOriginal = imageDisplay.Image;
            populateRecentFiles();

            Width = Properties.Settings.Default.FormWidth > 0 ? Properties.Settings.Default.FormWidth : 675;
            Height = Properties.Settings.Default.FormHeight > 0 ? Properties.Settings.Default.FormHeight : 375;
            splitContainer1.SplitterDistance = Properties.Settings.Default.SplitterDistance > 0 ? Properties.Settings.Default.SplitterDistance : 460;
            Top = Properties.Settings.Default.SplitterDistance > -5 ? Properties.Settings.Default.FormTop : 20;
            Left = Properties.Settings.Default.SplitterDistance > -5 ? Properties.Settings.Default.FormLeft : 20;

            foreach (string s in _possibleParams)
            {
                ToolStripMenuItem addThis = new ToolStripMenuItem(s, null, addParameter_Click);
                addParameterMenuItem.DropDownItems.Add(addThis);
            }

            foreach (string s in _possibleStates)
            {
                ToolStripMenuItem addThis = new ToolStripMenuItem(s, null, addState_Click);
                addStateMenuItem.DropDownItems.Add(addThis);
            }

            foreach (string s in _possibleFlags)
            {
                ToolStripMenuItem addThis = new ToolStripMenuItem(s, null, addFlagOrAction_Click);
                addFlagMenuItem.DropDownItems.Add(addThis);
            }

            foreach (string s in _possibleActions)
            {
                ToolStripMenuItem addThis = new ToolStripMenuItem(s, null, addFlagOrAction_Click);
                addActionMenuItem.DropDownItems.Add(addThis);
            }
        }

        // event handler for add flags
        private void addFlagOrAction_Click(object sender, EventArgs e)
        {
            ToolStripMenuItem paramToAdd = sender as ToolStripMenuItem;
            if (treeView.SelectedNode != null && treeView.SelectedNode.Tag.Equals("Parameter") && paramToAdd != null)
            {
                treeView.SelectedNode.Nodes.Add(getNewNode(paramToAdd.Text, "Value"));
                treeView.SelectedNode.Expand();
            }
        }

        // event handler for add parameter
        private void addParameter_Click(object sender, EventArgs e)
        {
            ToolStripMenuItem paramToAdd = sender as ToolStripMenuItem;
            if (treeView.SelectedNode != null && treeView.SelectedNode.Tag.Equals("State") && paramToAdd != null)
            {
                treeView.SelectedNode.Nodes.Add(getNewNode(paramToAdd.Text, "Parameter"));
                treeView.SelectedNode.Expand();
            }
            else
                MessageBox.Show("Please select a STATE in the tree to add the parameter to.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        // file > import
        private void menuImport_Click(object sender, EventArgs e)
        {
            try
            {
                OpenFileDialog openFile = new OpenFileDialog
                                              {
                                                  InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                                  Filter = "Sprite Files(*.spr;*.spi)|*.spr;*.spi|All files (*.*)|*.*",
                                                  RestoreDirectory = true,
                                                  Multiselect = false
                                              };
                DialogResult result = openFile.ShowDialog();
                if (result == DialogResult.OK)
                    loadSprite(openFile.FileName);
                sortTree();
            }
            catch (FileNotFoundException ex)
            {
                statusLabel.Text = "Error: File not found.";
                MessageBox.Show("Error: File not found.\n" + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        // helper to load spr files and populate controls
        private void loadSprite(string fileLocation)
        {
            treeView.Nodes.Clear();

            //read file into string and close
            StreamReader myFile = new StreamReader(fileLocation);
            string jsonString = myFile.ReadToEnd();
            jsonString = prettifyJSON(jsonString);
            myFile.Close();

            // deserialize jsonString into Sprite instance and add tree root
            Sprite sprizite = JsonConvert.DeserializeObject<Sprite>(jsonString);
            sprizite.fileName = fileLocation;
            sprizite.safeFileName = Path.GetFileName(fileLocation);
            treeView.Nodes.Add(getNewNode(sprizite.safeFileName, "File"));

            // get list of states and iterate over states
            List<string> validatedStateList = getStatesFromJSONString(jsonString);
            int stateNodeIndex = 0;
            foreach (string state in validatedStateList)
            {
                // add SPRITE_* nodes
                treeView.Nodes[0].Nodes.Add(getNewNode(state, "State"));

                // Special Case: Preloaded STATES ( META and DEFAULT )
                if (state.Equals("SPRITE_META_DATA") || state.Equals("SPRITE_STATE_DEFAULT"))
                {
                    // list the properties to populate
                    List<string> propList = sprizite.getNonNullProperties(state.Equals("SPRITE_META_DATA") ? "meta" : "default", sprizite);

                    // add parameterName nodes for each state
                    foreach (string vs in propList)
                    {
                        if (vs.Equals("fixtures") || vs.Equals("credits") || vs.Equals("spawn"))
                            treeView.Nodes[0].Nodes[stateNodeIndex].Nodes.Add(getNewNode(vs, "Index"));
                        else
                            treeView.Nodes[0].Nodes[stateNodeIndex].Nodes.Add(getNewNode(vs, "Parameter"));
                    }

                    // additional processing for subnodes in each root depending on if property is: value, array, or subarray
                    int k = 0;
                    foreach (TreeNode n in treeView.Nodes[0].Nodes[stateNodeIndex].Nodes)
                    {
                        string prop = n.Text;

                        //process values
                        string val = fetchParameter(sprizite, prop);

                        // contains array or sub array - extra processing
                        if (val.Equals("||ERROR||"))
                        {
                            // BEGIN process sub arrays (credits, fixtures, spawn)
                            if (prop.Equals("credits"))
                            {
                                for (int z = 0; z < sprizite.SPRITE_META_DATA.credits.Count; z++)
                                {
                                    treeView.Nodes[0].Nodes[stateNodeIndex].Nodes[k].Nodes.Add(getNewNode("[" + (z + 1) + "]", "Index"));

                                    if (sprizite.SPRITE_META_DATA.credits[z].author != null)
                                    {
                                        treeView.Nodes[0].Nodes[stateNodeIndex].Nodes[k].Nodes[z].Nodes.Add(getNewNode("author", "Parameter"));
                                        treeView.Nodes[0].Nodes[stateNodeIndex].Nodes[k].Nodes[z].Nodes[0].Nodes.Add(getNewNode(sprizite.SPRITE_META_DATA.credits[z].author, "Value"));
                                    }
                                    if (sprizite.SPRITE_META_DATA.credits[z].description != null)
                                    {
                                        treeView.Nodes[0].Nodes[stateNodeIndex].Nodes[k].Nodes[z].Nodes.Add(getNewNode("description", "Parameter"));
                                        treeView.Nodes[0].Nodes[stateNodeIndex].Nodes[k].Nodes[z].Nodes[1].Nodes.Add(getNewNode(sprizite.SPRITE_META_DATA.credits[z].description, "Value"));
                                    }
                                    if (sprizite.SPRITE_META_DATA.credits[z].url != null)
                                    {
                                        treeView.Nodes[0].Nodes[stateNodeIndex].Nodes[k].Nodes[z].Nodes.Add(getNewNode("url", "Parameter"));
                                        treeView.Nodes[0].Nodes[stateNodeIndex].Nodes[k].Nodes[z].Nodes[2].Nodes.Add(getNewNode(sprizite.SPRITE_META_DATA.credits[z].url, "Value"));
                                    }
                                }
                                k++;
                                continue;
                            }
                            if (prop.Equals("fixtures"))
                            {
                                for (int z = 0; z < sprizite.SPRITE_STATE_DEFAULT.fixtures.Count; z++)
                                {
                                    treeView.Nodes[0].Nodes[stateNodeIndex].Nodes[k].Nodes.Add(getNewNode("[" + (z + 1) + "]", "Index"));

                                    if (sprizite.SPRITE_STATE_DEFAULT.fixtures[z].x != null)
                                    {
                                        treeView.Nodes[0].Nodes[stateNodeIndex].Nodes[k].Nodes[z].Nodes.Add(getNewNode("x", "Parameter"));
                                        treeView.Nodes[0].Nodes[stateNodeIndex].Nodes[k].Nodes[z].Nodes[0].Nodes.Add(getNewNode(sprizite.SPRITE_STATE_DEFAULT.fixtures[z].x, "Value"));
                                    }
                                    if (sprizite.SPRITE_STATE_DEFAULT.fixtures[z].y != null)
                                    {
                                        treeView.Nodes[0].Nodes[stateNodeIndex].Nodes[k].Nodes[z].Nodes.Add(getNewNode("y", "Parameter"));
                                        treeView.Nodes[0].Nodes[stateNodeIndex].Nodes[k].Nodes[z].Nodes[1].Nodes.Add(getNewNode(sprizite.SPRITE_STATE_DEFAULT.fixtures[z].y, "Value"));
                                    }
                                    if (sprizite.SPRITE_STATE_DEFAULT.fixtures[z].w != null)
                                    {
                                        treeView.Nodes[0].Nodes[stateNodeIndex].Nodes[k].Nodes[z].Nodes.Add(getNewNode("w", "Parameter"));
                                        treeView.Nodes[0].Nodes[stateNodeIndex].Nodes[k].Nodes[z].Nodes[2].Nodes.Add(getNewNode(sprizite.SPRITE_STATE_DEFAULT.fixtures[z].w, "Value"));
                                    }
                                    if (sprizite.SPRITE_STATE_DEFAULT.fixtures[z].h != null)
                                    {
                                        treeView.Nodes[0].Nodes[stateNodeIndex].Nodes[k].Nodes[z].Nodes.Add(getNewNode("h", "Parameter"));
                                        treeView.Nodes[0].Nodes[stateNodeIndex].Nodes[k].Nodes[z].Nodes[3].Nodes.Add(getNewNode(sprizite.SPRITE_STATE_DEFAULT.fixtures[z].h, "Value"));
                                    }
                                }
                                k++;
                                continue;
                            }
                            if (prop.Equals("spawn"))
                            {
                                for (int z = 0; z < sprizite.SPRITE_META_DATA.spawn.Count; z++)
                                {
                                    treeView.Nodes[0].Nodes[stateNodeIndex].Nodes[k].Nodes.Add(getNewNode("[" + (z + 1) + "]", "Index"));
                                    
                                    if (sprizite.SPRITE_META_DATA.spawn[z].uri != null)
                                    {
                                        treeView.Nodes[0].Nodes[stateNodeIndex].Nodes[k].Nodes[z].Nodes.Add(getNewNode("uri", "Parameter"));
                                        treeView.Nodes[0].Nodes[stateNodeIndex].Nodes[k].Nodes[z].Nodes[0].Nodes.Add(getNewNode(sprizite.SPRITE_META_DATA.spawn[z].uri, "Value"));
                                    }
                                    if (sprizite.SPRITE_META_DATA.spawn[z].spawnX != null)
                                    {
                                        treeView.Nodes[0].Nodes[stateNodeIndex].Nodes[k].Nodes[z].Nodes.Add(getNewNode("spawnX", "Parameter"));
                                        treeView.Nodes[0].Nodes[stateNodeIndex].Nodes[k].Nodes[z].Nodes[1].Nodes.Add(getNewNode(sprizite.SPRITE_META_DATA.spawn[z].spawnX, "Value"));
                                    }
                                    if (sprizite.SPRITE_META_DATA.spawn[z].spawnY != null)
                                    {
                                        treeView.Nodes[0].Nodes[stateNodeIndex].Nodes[k].Nodes[z].Nodes.Add(getNewNode("spawnY", "Parameter"));
                                        treeView.Nodes[0].Nodes[stateNodeIndex].Nodes[k].Nodes[z].Nodes[2].Nodes.Add(getNewNode(sprizite.SPRITE_META_DATA.spawn[z].spawnY, "Value"));
                                    }
                                    if (sprizite.SPRITE_META_DATA.spawn[z].spawnExplode != null)
                                    {
                                        treeView.Nodes[0].Nodes[stateNodeIndex].Nodes[k].Nodes[z].Nodes.Add(getNewNode("spawnExplode", "Parameter"));
                                        treeView.Nodes[0].Nodes[stateNodeIndex].Nodes[k].Nodes[z].Nodes[3].Nodes.Add(getNewNode(sprizite.SPRITE_META_DATA.spawn[z].spawnExplode, "Value"));
                                    }
                                }
                                k++;
                                continue;
                            }

                            // process arrays
                            if (prop.Equals("flags") || prop.Equals("actions"))
                            {
                                int arrayCount = 0;
                                if (prop.Equals("flags"))
                                    arrayCount = sprizite.SPRITE_META_DATA.flags.Count;
                                else if (prop.Equals("actions"))
                                    arrayCount = sprizite.SPRITE_META_DATA.actions.Count;

                                for (int z = 0; z < arrayCount; z++)
                                {
                                    if (prop.Equals("flags"))
                                        treeView.Nodes[0].Nodes[stateNodeIndex].Nodes[k].Nodes.Add(getNewNode(sprizite.SPRITE_META_DATA.flags[z], "Parameter"));
                                    else if (prop.Equals("actions"))
                                        treeView.Nodes[0].Nodes[stateNodeIndex].Nodes[k].Nodes.Add(getNewNode(sprizite.SPRITE_META_DATA.actions[z], "Value"));
                                }
                                k++; continue;
                            }
                        }
                        treeView.Nodes[0].Nodes[stateNodeIndex].Nodes[k].Nodes.Add(getNewNode(val, "Value"));
                        k++;
                    }
                    stateNodeIndex++;
                    continue;
                } // end processing of META and DEFAULT

                // SPRITE_STATE_* detected - start processing using JTokens
                SpriteState addThis = new SpriteState(state);

                // Special Case: check if state is empty
                if (!JObject.Parse(jsonString)[state].Any()) { stateNodeIndex++; continue; }

                JToken iteratorToken = JObject.Parse(jsonString)[state].First();

                int j = 0;
                // check each token and parse
                while (iteratorToken != null)
                {
                    string propName = ((JProperty)iteratorToken).Name;
                    string propValue = ((JProperty)iteratorToken).Value.ToString();
                    treeView.Nodes[0].Nodes[stateNodeIndex].Nodes.Add(getNewNode(propName, "Parameter"));
                    treeView.Nodes[0].Nodes[stateNodeIndex].Nodes[j].Nodes.Add(getNewNode(propValue, "Value"));
                    addThis.setParameter(propName, propValue);

                    j++;
                    iteratorToken = iteratorToken.Next;
                }
                sprizite.addNewState(addThis);
                stateNodeIndex++;
            }

            // prepare controls by clearing them
            zoomSlider.Value = 1;
            _images.Clear();
            _imageLocations.Clear();
            _dependencies.Clear();

            // extract dependencies from uri params and load them
            TreeNode[] imagesToLoad = treeView.Nodes.Find("uri", true);
            foreach (TreeNode u in imagesToLoad)
            {
                if (u.FirstNode == null) continue;
                string imageLoc = u.FirstNode.Text;
                string fullImagePath = String.Concat(sprizite.fileName.Substring(0, sprizite.fileName.LastIndexOf("\\", StringComparison.Ordinal)), "\\", imageLoc);
                if (_imageLocations.Contains(fullImagePath))
                    continue;

                // add *.spi files to associated files list
                if (fullImagePath.EndsWith("spi") || fullImagePath.EndsWith("spr") && !Path.GetFileName(fullImagePath).Equals(sprizite.safeFileName))
                {
                    ToolStripMenuItem dynamicMenuItem = new ToolStripMenuItem(Path.GetFileName(fullImagePath));
                    dynamicMenuItem.Image = Properties.Resources.favicon;
                    dynamicMenuItem.Tag = fullImagePath;
                    dynamicMenuItem.Click += dynamicMenuItem_Click;
                    _dependencies.Add(dynamicMenuItem);
                    continue;
                }
                if (File.Exists(fullImagePath))
                    _images.Add(new Bitmap(fullImagePath));
                _imageLocations.Add(fullImagePath);
            }

            // display DEFAULT image in picturebox
            if (_imageLocations.Any() && File.Exists(_imageLocations[0]))
            {
                imageDisplay.Image = new Bitmap(_imageLocations[0]);
                imageDisplay.Tag = sprizite.SPRITE_STATE_DEFAULT.uri;
                imageDisplay.Width = imageDisplay.Image.Width;
                imageDisplay.Height = imageDisplay.Image.Height;
                _imageOriginal = imageDisplay.Image;
            }

            // all done populating controls - finalize
            statusLabel.Text = sprizite.safeFileName + " was imported successfully.";
            treeView.Nodes[0].Expand();
            addMRU(sprizite.fileName);
            populateRecentFiles();
            _workingFile = sprizite.fileName;
        }

        // used for processing META_DATA and STATE_DEFAULT
        private static string fetchParameter(Sprite sprizite, string prop)
        {
            string val = "||ERROR||";
            if (prop.Equals("version"))
                val = sprizite.SPRITE_META_DATA.version;
            if (prop.Equals("uri"))
                val = sprizite.SPRITE_STATE_DEFAULT.uri;
            if (prop.Equals("cropX"))
                val = sprizite.SPRITE_STATE_DEFAULT.cropX;
            if (prop.Equals("cropY"))
                val = sprizite.SPRITE_STATE_DEFAULT.cropY;
            if (prop.Equals("cropW"))
                val = sprizite.SPRITE_STATE_DEFAULT.cropW;
            if (prop.Equals("cropH"))
                val = sprizite.SPRITE_STATE_DEFAULT.cropH;
            if (prop.Equals("transparent"))
                val = sprizite.SPRITE_STATE_DEFAULT.transparent;
            if (prop.Equals("frameDelay"))
                val = sprizite.SPRITE_STATE_DEFAULT.frameDelay;
            if (prop.Equals("sizeMultiplier"))
                val = sprizite.SPRITE_STATE_DEFAULT.sizeMultiplier;
            if (prop.Equals("autoClose"))
                val = sprizite.SPRITE_STATE_DEFAULT.autoClose;
            if (prop.Equals("isChain"))
                val = sprizite.SPRITE_STATE_DEFAULT.isChain;
            if (prop.Equals("flipX"))
                val = sprizite.SPRITE_STATE_DEFAULT.flipX;
            if (prop.Equals("sizeDivider"))
                val = sprizite.SPRITE_STATE_DEFAULT.sizeDivider;
            if (prop.Equals("offsX"))
                val = sprizite.SPRITE_STATE_DEFAULT.offsX;
            if (prop.Equals("offsY"))
                val = sprizite.SPRITE_STATE_DEFAULT.offsY;
            if (prop.Equals("usePrevious"))
                val = sprizite.SPRITE_STATE_DEFAULT.usePrevious;
            if (prop.Equals("walkMultiplier"))
                val = sprizite.SPRITE_STATE_DEFAULT.walkMultiplier;
            return val;
        }

        // file > exit
        private void menuExit_Click(object sender, EventArgs e) { Application.Exit(); }  // TO DO:  Save App settings

        // file > new
        private void newFileMenuItem_Click(object sender, EventArgs e)
        {

            // confirm with user that unsaved changed will be lost
            if (treeView.Nodes.Count != 0)
            {
                DialogResult result = MessageBox.Show("You will lose any unsaved changes to " + treeView.Nodes[0].Text + ".", "New File", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
                if (result == DialogResult.Cancel)
                    return;
            }

            // display sprite wizard, get values, process values
            using (var form = new frmWizard())
            {
                DialogResult result2 = form.ShowDialog();
                if (result2 == DialogResult.OK)
                {
                    // clear controls for new sprite
                    treeView.Nodes.Clear();
                    _images.Clear();
                    _imageLocations.Clear();
                    imageDisplay.Image = Properties.Resources.favicon;
                    imageDisplay.Height = imageDisplay.Image.Height;
                    imageDisplay.Width = imageDisplay.Image.Width;

                    // store values from Sprite Wizard
                    Dictionary<string, string> sprInfo = form._spriteInfo;
                    List<string> actions = form._actions;
                    List<string> flags = form._flags;
                    List<string> states = form._states;

                    // add required nodes
                    treeView.Nodes.Add(getNewNode(sprInfo["name"], "File"));
                    treeView.Nodes[0].Nodes.Add(getNewNode("SPRITE_META_DATA", "State"));
                    treeView.Nodes[0].Nodes.Add(getNewNode("SPRITE_STATE_DEFAULT", "State"));

                    foreach (KeyValuePair<string, string> i in sprInfo)
                    {
                        switch (i.Key)
                        {
                            case "name":
                                break;
                            case "version":
                                treeView.Nodes[0].Nodes["SPRITE_META_DATA"].Nodes.Add(getNewNode("version", "Parameter"));
                                treeView.Nodes[0].Nodes["SPRITE_META_DATA"].Nodes["version"].Nodes.Add(getNewNode(i.Value, "Value"));
                                break;
                            case "author":
                            case "description":
                            case "url":
                                if (treeView.Nodes[0].Nodes["SPRITE_META_DATA"].Nodes["credits"] == null)
                                {
                                    treeView.Nodes[0].Nodes["SPRITE_META_DATA"].Nodes.Add(getNewNode("credits", "Index"));
                                    treeView.Nodes[0].Nodes["SPRITE_META_DATA"].Nodes["credits"].Nodes.Add(getNewNode("[1]", "Index"));
                                }
                                treeView.Nodes[0].Nodes["SPRITE_META_DATA"].Nodes["credits"].Nodes["[1]"].Nodes.Add(getNewNode(i.Key, "Value"));
                                treeView.Nodes[0].Nodes["SPRITE_META_DATA"].Nodes["credits"].Nodes["[1]"].Nodes[i.Key].Nodes.Add(getNewNode(i.Value, "Value"));
                                break;
                        }
                    }

                    if (actions.Count != 0)
                    {
                        treeView.Nodes[0].Nodes["SPRITE_META_DATA"].Nodes.Add(getNewNode("actions", "Parameter"));
                        foreach (string a in actions)
                            treeView.Nodes[0].Nodes["SPRITE_META_DATA"].Nodes["actions"].Nodes.Add(getNewNode(a, "Value"));
                    }

                    if (flags.Count != 0)
                    {
                        treeView.Nodes[0].Nodes["SPRITE_META_DATA"].Nodes.Add(getNewNode("flags", "Parameter"));
                        foreach (string f in flags)
                            treeView.Nodes[0].Nodes["SPRITE_META_DATA"].Nodes["flags"].Nodes.Add(getNewNode(f, "Value"));
                    }

                    if (states.Count != 0)
                    {
                        foreach (string s in states)
                            treeView.Nodes[0].Nodes.Add(getNewNode(s, "State"));
                    }
                    sortTree();
                    treeView.Nodes[0].Expand();
                }
            }
        }

        // image click
        private void imageDisplay_Click(object sender, EventArgs e)
        {
            if (_request.Equals("grabHTML"))
            {
                string[] coords = statusCoordsLabel.Text.Split(new[] { 'X', 'Y', ':', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                Bitmap getThisColor = (Bitmap)imageDisplay.Image;
                Color chosenColor = getThisColor.GetPixel(int.Parse(coords[0]), int.Parse(coords[1]));
                if (treeView.SelectedNode == null) { MessageBox.Show("Please select a valid Tree Node to place the value in.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
                string chosenColorHTML = hexConverter(chosenColor);
                TreeNode addHere = treeView.SelectedNode;
                if (addHere.Tag.Equals("Value"))
                    addHere.Text = chosenColorHTML;
                else
                {
                    if (addHere.Tag.Equals("Parameter") && addHere.Nodes.Count == 0)
                        addHere.Nodes.Add(getNewNode(chosenColorHTML, "Value"));
                    else
                    {
                        addHere.Nodes[0].Remove();
                        addHere.Nodes.Add(getNewNode(chosenColorHTML, "Value"));
                    }
                }
                _request = "";
                statusLabel.Text = chosenColorHTML + " was selected.";
            }
            else if (_request.Equals("grabX"))
            {
                string[] coords = statusCoordsLabel.Text.Split(new[] { 'X', 'Y', ':', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (treeView.SelectedNode == null) { MessageBox.Show("Please select a valid Tree Node to place the value in.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
                TreeNode addHere = treeView.SelectedNode;
                if (addHere.Tag.Equals("Value"))
                    addHere.Text = coords[0];
                else
                {
                    if (addHere.Tag.Equals("Parameter") && addHere.Nodes.Count == 0)
                        addHere.Nodes.Add(getNewNode(coords[0], "Value"));
                    else
                    {
                        addHere.Nodes[0].Remove();
                        addHere.Nodes.Add(getNewNode(coords[0], "Value"));
                    }
                }
                statusLabel.Text = treeView.SelectedNode.FullPath + " set to " + coords[0];
                _request = "";
            }
            else if (_request.Equals("grabY"))
            {
                string[] coords = statusCoordsLabel.Text.Split(new[] { 'X', 'Y', ':', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (treeView.SelectedNode == null) { MessageBox.Show("Please select a valid Tree Node to place the value in.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
                TreeNode addHere = treeView.SelectedNode;
                if (addHere.Tag.Equals("Value"))
                    addHere.Text = coords[1];
                else
                {
                    if (addHere.Tag.Equals("Parameter") && addHere.Nodes.Count == 0)
                        addHere.Nodes.Add(getNewNode(coords[1], "Value"));
                    else
                    {
                        addHere.Nodes[0].Remove();
                        addHere.Nodes.Add(getNewNode(coords[1], "Value"));
                    }
                }
                statusLabel.Text = treeView.SelectedNode.FullPath + " set to " + coords[0];
                _request = "";
            }
        }

        // tree clicked
        private void treeView_OnNodeMouseClick(object sender, TreeNodeMouseClickEventArgs e)
        {
            if (e.Button != MouseButtons.Right) return;
            Point p = new Point(e.X, e.Y);
            TreeNode node = treeView.GetNodeAt(p);
            Debug.WriteLine(node.Text + " is " + node.Tag);
            
            if (Convert.ToString(node.Tag).Equals("Parameter") && node.Text.Equals("actions"))
            {
                treeActionsContextMenu.Show(treeView, p);
                treeView.SelectedNode = node;
                return;
            }

            if (Convert.ToString(node.Tag).Equals("Parameter") && node.Text.Equals("flags"))
            {
                treeFlagsContextMenu.Show(treeView, p);
                treeView.SelectedNode = node;
                return;
            }

            switch (Convert.ToString(node.Tag))
            {
                case "File":
                    treeFileContextMenu.Show(treeView, p);
                    break;
                case "State":
                    treeStateContextMenu.Show(treeView, p);
                    break;
                case "Parameter":
                    treeParamContextMenu.Show(treeView, p);
                    break;
                case "Value":
                    treeValueContextMenu.Show(treeView, p);
                    break;
                case "Index":
                    treeIndexContextMenu.Show(treeView, p);
                    break;
            }
            treeView.SelectedNode = node;
        }

        void dynamicMenuItem_Click(object sender, EventArgs e)
        {
            ToolStripMenuItem sent = sender as ToolStripMenuItem;
            if (sent != null && File.Exists(sent.Tag.ToString()))
                loadSprite(sent.Tag.ToString());
            else
                MessageBox.Show("Error: File not found.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);

        }

        // help > visit avian website
        private void checkForUpdatesToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Process.Start("http://avian.netne.net/index.php?p=programming&pid=7");
        }

        // help > submit sprite
        private void visitSpriteForumsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Process.Start("http://sprites.caustik.com/forum/10-character-submissions/");
        }

        // tree context menu > expand all
        private void expandAllToolStripMenuItem_Click(object sender, EventArgs e) { treeView.ExpandAll(); }

        // tree context menu > collapse all
        private void collapseAllToolStripMenuItem_Click(object sender, EventArgs e) { treeView.CollapseAll(); }

        // tools > prettify mouseover
        private void prettifySPRToolStripMenuItem_OnMouseEnter(object sender, EventArgs e)
        {
            statusLabel.Text = "Give SPR file proper indentation, spacing, and line breaks.";
        }

        // tools > prettify mouseleave
        private void setStatusLabelDefault_OnMouseLeave(object sender, EventArgs e) { statusLabel.Text = "Sprite Editor By Avian"; }

        // tools > preffity clicked
        private void prettifySPRToolStripMenuItem_Click(object sender, EventArgs e)
        {
            try
            {
                OpenFileDialog openFile = new OpenFileDialog
                                              {
                                                  InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                                  Filter = "Sprite Files(*.spr;*.spi;*.json)|*.spr;*.spi;*.json|All files (*.*)|*.*",
                                                  RestoreDirectory = true,
                                                  Multiselect = false
                                              };
                DialogResult result = openFile.ShowDialog();
                if (result == DialogResult.OK)
                {
                    //read file to string then close
                    StreamReader myFile = new StreamReader(openFile.FileName);
                    string jsonString = myFile.ReadToEnd();
                    myFile.Close();

                    // parse into JSON object, serialize, save file
                    File.WriteAllText(openFile.FileName, prettifyJSON(jsonString));
                }
                statusLabel.Text = openFile.SafeFileName + " was cleaned and updated.";
                openFile.Dispose();
            }
            catch (FileNotFoundException ex)
            {
                statusLabel.Text = "Error: File not found!";
                MessageBox.Show("Error: File not found.\n\n" + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        // helper method for tree population
        public TreeNode getNewNode(string nameText, string tag)
        {
            TreeNode node = new TreeNode(nameText)
                           {Name = nameText, Tag = tag};
            return node;
        }

        // helper method to Format JSON
        public static string prettifyJSON(string formatThis) { return (JsonConvert.SerializeObject(JObject.Parse(formatThis), Formatting.Indented)); }

        // helper method to convert Color to Hex
        public static String hexConverter(Color c) { return c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2"); }

        // helper method for file > import
        public static List<string> getStatesFromJSONString(string jsonString)
        {
            JObject root = JObject.Parse(jsonString);
            IList<JToken> states = root.Children().ToList();

            List<string> rootStates = new List<string>();

            JToken jtoken = states.First();

            while (jtoken != null)
            {
                rootStates.Add(((JProperty)jtoken).Name);
                jtoken = jtoken.Next;
            }
            return rootStates;
        }

        // file > save as clicked
        private void saveFileMenuItem_Click(object sender, EventArgs e)
        {
            if (treeView.Nodes.Count == 0)
            {
                statusLabel.Text = "Cannot save. Empty file!";
                return;
            }
            SaveFileDialog saveFileDialog1 = new SaveFileDialog
                                                 {
                                                     Filter = "Sprite Files(*.spr;*.spi)|*.spr;*.spi|All files (*.*)|*.*",
                                                     FilterIndex = 1,
                                                     RestoreDirectory = true,
                                                     FileName = treeView.Nodes[0].Text
                                                 };

            if (saveFileDialog1.ShowDialog() == DialogResult.OK)
            {
                StreamWriter file = new StreamWriter(saveFileDialog1.FileName);
                file.WriteLine(writeJsonFromTree(treeView));
                file.Close();
                _workingFile = saveFileDialog1.FileName;
                addMRU(saveFileDialog1.FileName);
                populateRecentFiles();
            }
            Debug.WriteLine(writeJsonFromTree(treeView));
        }

        // helper for saving files
        private static string writeJsonFromTree(TreeView tree)
        {
            StringWriter sw = new StringWriter(new StringBuilder());
            JsonWriter jsonWriter = new JsonTextWriter(sw);
            jsonWriter.Formatting = Formatting.Indented;

            // START WRITING JSON
            jsonWriter.WriteStartObject();

            if (tree.Nodes.Count <= 0) // TREE EMPTY! SHOW ERROR!
                return "Cannot write empty sprite!";

            if (tree.Nodes[0].Nodes.Count < 2)
                return "Sprite Requires at least META_DATA and STATE_DEFAULT!";

            TreeNode stateNode = tree.Nodes[0].Nodes[0];

            // loop through each SPRITE_* node and process
            while (stateNode != null)
            {
                jsonWriter.WritePropertyName(stateNode.Text);
                jsonWriter.WriteStartObject();

                // Check if state node has no subnodes
                if (stateNode.Nodes.Count == 0) { jsonWriter.WriteEndObject(); stateNode = stateNode.NextNode; continue; }

                // check properties and write arrays / subarrays
                TreeNode parameterNode = stateNode.Nodes[0];
                while (parameterNode != null)
                {
                    // handle properties with sub arrays
                    if (parameterNode.Text.Equals("credits") || parameterNode.Text.Equals("fixtures") || parameterNode.Text.Equals("spawn"))
                    {

                        int credCount = parameterNode.Nodes.Count;
                        if (credCount == 0) { stateNode = stateNode.NextNode; continue; }
                        TreeNode indexNode = parameterNode.Nodes[0];

                        jsonWriter.WritePropertyName(parameterNode.Text);
                        jsonWriter.WriteStartArray();
                        while (indexNode != null)
                        {
                            if (indexNode.Nodes.Count == 0) { indexNode = indexNode.NextNode; }
                            TreeNode credProps = indexNode.Nodes[0];
                            jsonWriter.WriteStartObject();
                            int i = 0;
                            while (credProps != null)
                            {
                                if (indexNode.Nodes[i].Nodes.Count == 0) { i++; credProps = credProps.NextNode; continue; }
                                jsonWriter.WritePropertyName(indexNode.Nodes[i].Text);
                                jsonWriter.WriteValue(indexNode.Nodes[i].Nodes[0].Text);
                                i++;
                                credProps = credProps.NextNode;
                            }
                            jsonWriter.WriteEndObject();
                            indexNode = indexNode.NextNode;
                        }
                        jsonWriter.WriteEndArray();
                        parameterNode = parameterNode.NextNode;
                        continue;
                    }
                    if (parameterNode.Text.Equals("actions") || parameterNode.Text.Equals("flags")) // handle array properties
                    {
                        if (parameterNode.Nodes.Count == 0) { parameterNode = parameterNode.NextNode; continue; }
                        jsonWriter.WritePropertyName(parameterNode.Text);
                        jsonWriter.WriteStartArray();
                        TreeNode propName = parameterNode.Nodes[0];
                        while (propName != null)
                        {
                            jsonWriter.WriteValue(propName.Text);
                            propName = propName.NextNode;
                        }
                        jsonWriter.WriteEndArray();
                        parameterNode = parameterNode.NextNode;
                        continue;
                    }

                    // singular property / value
                    TreeNode stateValue = parameterNode.Nodes.Count != 0 ? parameterNode.Nodes[0] : new TreeNode("");
                    jsonWriter.WritePropertyName(parameterNode.Text);
                    jsonWriter.WriteValue(stateValue.Text);
                    parameterNode = parameterNode.NextNode;
                }
                jsonWriter.WriteEndObject();
                stateNode = stateNode.NextNode;
            }
            jsonWriter.WriteEndObject();
            return sw.ToString();
        }

        // image mouseover
        private void pictureBox_MouseOver(object sender, MouseEventArgs e)
        {
            statusCoordsLabel.Text = "X: " + (e.X / zoomSlider.Value) + " Y: " + (e.Y / zoomSlider.Value);
        }

        // helper for zoom functionality
        //public Image pictureBoxZoom(Image img, Size size)
        //{
        //    Bitmap bm = new Bitmap(img, Convert.ToInt32(img.Width * size.Width), Convert.ToInt32(img.Height * size.Height));
        //    Graphics grap = Graphics.FromImage(bm);
        //    grap.InterpolationMode = InterpolationMode.HighQualityBicubic;
        //    return bm;
        //}

        // view > zoom in selected
        private void zoomInToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (zoomSlider.Value == zoomSlider.Maximum)
                return;
            zoomSlider.Value++;
            imageDisplay.Width = imageDisplay.Image.Width * zoomSlider.Value;
            imageDisplay.Height = imageDisplay.Image.Height * zoomSlider.Value;
        }

        // veiw > zoom out selected
        private void zoomOutToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (zoomSlider.Value == zoomSlider.Minimum)
                return;
            zoomSlider.Value--;
            imageDisplay.Width = imageDisplay.Image.Width * zoomSlider.Value;
            imageDisplay.Height = imageDisplay.Image.Height * zoomSlider.Value;
        }

        // view > actual size selected
        private void actualSizeToolStripMenuItem_Click(object sender, EventArgs e)
        {
            imageDisplay.Image = _imageOriginal;
            imageDisplay.Width = imageDisplay.Image.Width;
            imageDisplay.Height = imageDisplay.Image.Height;
            zoomSlider.Value = 1;
        }

        // event handler for recently loaded images
        private void imageDropDownMenu_Click(object sender, EventArgs e)
        {
            ToolStripMenuItem mi = sender as ToolStripMenuItem;
            if (mi != null)
                imageDisplay.Image = new Bitmap(mi.Tag.ToString());
            _imageOriginal = imageDisplay.Image;
            zoomSlider.Value = 1;
            imageDisplay.Width = imageDisplay.Image.Width;
            imageDisplay.Height = imageDisplay.Image.Height;
            statusLabel.Text = Text + " loaded OK.";
        }

        // helper for adding recently used files
        private static void addMRU(string fileLoc)
        {
            List<string> g = new List<string>(Properties.Settings.Default.MRU.Split('?'));
            
            if (g.Any(i => i.Equals(fileLoc)))
                return;
            if (g.Count > 3)
                g.RemoveAt(0);

            Properties.Settings.Default.MRU = g[0].Equals("") ? fileLoc : string.Concat(string.Join("?", g), "?", fileLoc);
            Properties.Settings.Default.Save();
        }

        // helper for populating recently used files
        private void populateRecentFiles()
        {
            recentSpritesToolStripMenuItem.DropDownItems.Clear();
            string[] split = Properties.Settings.Default.MRU.Split('?');

            foreach (string rs in split)
            {
                if (rs.Equals("") || !File.Exists(rs))
                    continue;
                ToolStripMenuItem dynamicMenuItem = new ToolStripMenuItem(Path.GetFileName(rs));
                dynamicMenuItem.Click += dynamicMenuItem_Click;
                dynamicMenuItem.Tag = rs;
                recentSpritesToolStripMenuItem.DropDownItems.Add(dynamicMenuItem);
            }
        }

        // tools > sprite preview selected
        private void spritePreviewToolStripMenuItem_Click(object sender, EventArgs e)
        {
            using (var form = new frmPreview(_images, _imageLocations, ref treeView))
            {
                form.ShowDialog();
            }
        }

        // view > hide tree selected
        private void hideTreeToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (splitContainer1.Panel2Collapsed)
            {
                hideTreeToolStripMenuItem.Text = "Hide Tree";
                splitContainer1.Panel2Collapsed = false;
            }
            else
            {
                hideTreeToolStripMenuItem.Text = "Show Tree";
                splitContainer1.Panel2Collapsed = true;
            }
        }

        // tools > test sprite selected
        private void testSpriteToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (treeView.Nodes.Count <= 0)
                return;
            if (_workingFile.Equals(""))
            {
                MessageBox.Show("Please use File > Save As to set the working file location.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            string folder = Path.GetDirectoryName(_workingFile);
            TextWriter tw = new StreamWriter(_workingFile);
            tw.WriteLine(writeJsonFromTree(treeView));
            tw.Close();

            int i = 0;
            foreach (Bitmap b in _images)
            {
                string loc = folder + "\\" + Path.GetFileName(_imageLocations[i++]);
                if(!File.Exists(loc))
                    b.Save(loc);
            }

            Process.Start(_workingFile);
        }

        // event handler for context menu EDIT item
        private void manualEntryToolStripMenuItem1_Click(object sender, EventArgs e)
        {
            if (treeView.SelectedNode != null)
                treeView.SelectedNode.BeginEdit();
        }

        // edit > grab X value selected
        private void grabXValueToolStripMenuItem1_Click(object sender, EventArgs e)
        {
            if (treeView.SelectedNode != null && (treeView.SelectedNode.Tag.Equals("Parameter") || treeView.SelectedNode.Tag.Equals("Value")))
            {
                _request = "grabX";
                statusLabel.Text = "Click the image to set a X value.";
            }
            else
            {
                MessageBox.Show("Please select a Parameter in the tree to store the X value in", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        // edit > grab Y value selected
        private void grabYValueToolStripMenuItem1_Click(object sender, EventArgs e)
        {
            if (treeView.SelectedNode != null && (treeView.SelectedNode.Tag.Equals("Parameter") || treeView.SelectedNode.Tag.Equals("Value")))
            {
                _request = "grabY";
                statusLabel.Text = "Click the image to set a Y value.";
            }
            else
            {
                MessageBox.Show("Please select a Parameter in the tree to store the X value in", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        // edit > grab color selected
        private void grabColorToolStripMenuItem1_Click(object sender, EventArgs e)
        {
            if (treeView.SelectedNode != null && (treeView.SelectedNode.Tag.Equals("Parameter") || treeView.SelectedNode.Tag.Equals("Value")))
            {
                _request = "grabHTML";
                statusLabel.Text = "Click the image to grab color value.";
            }
            else
            {
                MessageBox.Show("Please select a Parameter in the tree to store the X value in", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        // view > open image selected
        private void openImageToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (treeView.Nodes.Count == 0)
            {
                MessageBox.Show("Cannot load picture. Go to File > New then try again.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            try
            {
                OpenFileDialog openFile = new OpenFileDialog
                                              {
                                                  InitialDirectory = "c:\\",
                                                  Filter = "Image Files(*.png;*.jpg;*.gif;*.bmp)|*.png;*.jpg;*.gif;*.bmp|All files (*.*)|*.*",
                                                  RestoreDirectory = true,
                                                  Multiselect = false
                                              };
                DialogResult result = openFile.ShowDialog();
                if (result == DialogResult.OK)
                {
                    imageDisplay.Image = new Bitmap(openFile.FileName);
                    imageDisplay.Tag = openFile.SafeFileName;
                    imageDisplay.Width = imageDisplay.Image.Width;
                    imageDisplay.Height = imageDisplay.Image.Height;

                    statusLabel.Text = openFile.SafeFileName + " loaded " + result + ".";

                    zoomSlider.Value = 1;
                    _imageOriginal = imageDisplay.Image;
                    _images.Add(new Bitmap(openFile.FileName));
                    _imageLocations.Add(openFile.FileName);
                }
                else
                {
                    statusLabel.Text = "File was not be loaded.";
                }
                openFile.Dispose();
            }
            catch (Exception ex)
            {
                statusLabel.Text = "Error: Picture could not be loaded.";
                MessageBox.Show("Error: File not found.\n\n" + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        // event handler to populate Recent Images
        private void viewToolStripMenuItem_DropDownOpening(object sender, EventArgs e)
        {
            dependenciesToolStripMenuItem.DropDownItems.Clear();
            foreach (string i in _imageLocations)
            {
                ToolStripMenuItem addThis = new ToolStripMenuItem(Path.GetFileName(i), null, imageDropDownMenu_Click) { Tag = i, Image = Properties.Resources.generic_picture };
                dependenciesToolStripMenuItem.DropDownItems.Add(addThis);
            }
            foreach (ToolStripMenuItem d in _dependencies)
                dependenciesToolStripMenuItem.DropDownItems.Add(d);

            dependenciesToolStripMenuItem.Enabled = dependenciesToolStripMenuItem.DropDownItems.Count != 0;
        }

        // 
        private void useImageURIToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (treeView.SelectedNode != null && treeView.SelectedNode.Tag.Equals("Parameter"))
            {
                TreeNode addHere = treeView.SelectedNode;
                if (addHere.Nodes.Count == 0)
                    addHere.Nodes.Add(getNewNode(imageDisplay.Tag.ToString(), "Value"));
                else
                    addHere.Nodes[0].Text = imageDisplay.Tag.ToString();
                statusLabel.Text = "Operation successful.";
            }
            else
            {
                MessageBox.Show("Please select a Parameter in the tree to store the URI.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        // event handler for edit > delete state menu item
        private void deleteStateToolStripMenuItem_Click(object sender, EventArgs e)
        {
            treeView.SelectedNode.Remove();
        }

        // event handler for add state
        private void addState_Click(object sender, EventArgs e)
        {
            ToolStripMenuItem paramToAdd = sender as ToolStripMenuItem;
            if (treeView.Nodes.Count != 0 && paramToAdd != null)
                treeView.Nodes[0].Nodes.Add(getNewNode(paramToAdd.Text, "State"));
            sortTree();
        }

        private void addNewGroupToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (treeView.SelectedNode != null && treeView.SelectedNode.Tag.Equals("Index"))
            {
                TreeNode groupNode = treeView.SelectedNode;
                if (treeView.SelectedNode.Text.Contains("["))
                    groupNode = treeView.SelectedNode.Parent;
                int count = groupNode.Nodes.Count;
                groupNode.Nodes.Add(getNewNode(String.Concat("[", ++count, "]"), "Index"));

                if (groupNode.Text.Equals("fixtures"))
                {
                    groupNode.LastNode.Nodes.Add(getNewNode("x", "Parameter"));
                    groupNode.LastNode.Nodes.Add(getNewNode("y", "Parameter"));
                    groupNode.LastNode.Nodes.Add(getNewNode("w", "Parameter"));
                    groupNode.LastNode.Nodes.Add(getNewNode("h", "Parameter"));
                }
                else if (groupNode.Text.Equals("credits"))
                {
                    groupNode.LastNode.Nodes.Add(getNewNode("author", "Parameter"));
                    groupNode.LastNode.Nodes.Add(getNewNode("description", "Parameter"));
                    groupNode.LastNode.Nodes.Add(getNewNode("url", "Parameter"));
                }
                else if (groupNode.Text.Equals("spawn"))
                {
                    groupNode.LastNode.Nodes.Add(getNewNode("uri", "Parameter"));
                    groupNode.LastNode.Nodes.Add(getNewNode("spawnX", "Parameter"));
                    groupNode.LastNode.Nodes.Add(getNewNode("spawnY", "Parameter"));
                    groupNode.LastNode.Nodes.Add(getNewNode("spawnExplode", "Parameter"));
                }
            }
        }

        private void fileMenuItem_DropDownOpening(object sender, EventArgs e)
        {
            recentSpritesToolStripMenuItem.Enabled = recentSpritesToolStripMenuItem.DropDownItems.Count != 0;
        }

        // custom TreeNode sorter implementation
        public class NodeSorter : IComparer
        {
            public int Compare(object thisObj, object otherObj)
            {
                TreeNode thisNode = thisObj as TreeNode;
                TreeNode otherNode = otherObj as TreeNode;

                //if (thisNode.Text.Equals("SPRITE_META_DATA") || thisNode.Text.Equals("SPRITE_STATE_DEFAULT"))
                //    return 1;
                if (thisNode.Tag.Equals("Parameter") || thisNode.Tag.Equals("Value"))
                    return 0;

                return thisNode.Text.CompareTo(otherNode.Text);
            }
        }

        private void toolsToolStripMenuItem_DropDownOpening(object sender, EventArgs e)
        {
            if (treeView.Nodes.Count == 0 || !treeView.Nodes.Find("SPRITE_META_DATA", true).Any() || !treeView.Nodes.Find("SPRITE_STATE_DEFAULT", true).Any())
            {
                spritePreviewToolStripMenuItem.Enabled = false;
                testSpriteToolStripMenuItem.Enabled = false;
            }
            else
            {
                spritePreviewToolStripMenuItem.Enabled = true;
                testSpriteToolStripMenuItem.Enabled = true;
            }
        }

        private void spritePreviewToolStripMenuItem_MouseEnter(object sender, EventArgs e)
        {
            statusLabel.Text = "Show preview of each indivudal SPRITE_STATE";
        }

        private void testSpriteToolStripMenuItem_MouseEnter(object sender, EventArgs e)
        {
            statusLabel.Text = "Save Sprite/Images and Open it in sprites.exe";
        }

        private void checkForUpdatesToolStripMenuItem_MouseEnter(object sender, EventArgs e)
        {
            statusLabel.Text = "Visit Sprite Editor homepage.";
        }

        private void visitSpriteForumsToolStripMenuItem_MouseEnter(object sender, EventArgs e)
        {
            statusLabel.Text = "Visit Character Submissions Forum on sprites.caustik.com";
        }

        private void dependenciesToolStripMenuItem_MouseEnter(object sender, EventArgs e)
        {
            statusLabel.Text = "Open associated images, *.spi, and *.spr files";
        }

        private void loadedImagesToolStripMenuItem_MouseEnter(object sender, EventArgs e)
        {
            statusLabel.Text = "List of included image files in the *.spr file";
        }

        private void openImageToolStripMenuItem_MouseEnter(object sender, EventArgs e)
        {
            statusLabel.Text = "Add new image to current sprite";
        }

        private void sPRFileDocumentationToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Process.Start("http://sprites.caustik.com/topic/356-how-to-create-your-own-spr-files/");
        }

        private void sPRFileDocumentationToolStripMenuItem_MouseEnter(object sender, EventArgs e)
        {
            statusLabel.Text = "View *.spr file format specs in web browser";
        }

        private void saveToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (_workingFile.Equals(""))
            {
                MessageBox.Show("Please use File > Save As to set the working file.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (treeView.Nodes.Count == 0)
            {
                statusLabel.Text = "Cannot save. Empty file!";
                return;
            }

            StreamWriter file = new StreamWriter(_workingFile);
            file.WriteLine(writeJsonFromTree(treeView));
            file.Close();

            statusLabel.Text = "File successfully saved!";
        }

        private void incrementStateMenuItem_Click(object sender, EventArgs e)
        {
            string stateNo = _regex.Match(treeView.SelectedNode.Text).Value;
            string newState = stateNo.Equals("") ? string.Concat(treeView.SelectedNode.Text, "_0") : string.Concat(_regex.Replace(treeView.SelectedNode.Text, ""), int.Parse(stateNo) + 1);
            treeView.Nodes[0].Nodes.Add(getNewNode(newState, "State"));
            sortTree();
        }

        private void sortTree() { treeView.TreeViewNodeSorter = new NodeSorter(); }

        private void manualEntryToolStripMenuItem2_Click(object sender, EventArgs e)
        {
            ToolStripMenuItem s = sender as ToolStripMenuItem;
            if (s == null) return;
            treeView.SelectedNode.Nodes.Add(getNewNode("SPRITE_STATE_", s.Tag.ToString()));
            treeView.SelectedNode.Expand();
            treeView.SelectedNode.LastNode.BeginEdit();
        }

        private void moveUpMenuItem_Click(object sender, EventArgs e)
        {
            treeView.SelectedNode.MoveUp();
        }

        private void moveDownMenuItem_Click(object sender, EventArgs e)
        {
            treeView.SelectedNode.MoveDown();
        }

        private void frmMain_FormClosed(object sender, FormClosedEventArgs e)
        {
            Properties.Settings.Default.FormHeight = Height;
            Properties.Settings.Default.FormWidth = Width;
            Properties.Settings.Default.FormTop = Top;
            Properties.Settings.Default.FormLeft = Left;
            Properties.Settings.Default.SplitterDistance = splitContainer1.SplitterDistance;
            Properties.Settings.Default.Save();
            Debug.WriteLine("Top: " + Top + " Left: " + Left);
        }

        private void spawnToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (treeView.SelectedNode != null && treeView.SelectedNode.Tag.Equals("State") && !treeView.SelectedNode.Nodes.Find("spawn", true).Any())
            {
                treeView.SelectedNode.Expand();
                TreeNode spawnNode = getNewNode("spawn", "Index");
                TreeNode groupNode = getNewNode("[1]", "Index");
                treeView.SelectedNode.Nodes.Add(spawnNode);
                spawnNode.Nodes.Add(groupNode);
                groupNode.Nodes.Add(getNewNode("uri", "Parameter"));
                groupNode.Nodes.Add(getNewNode("spawnX", "Parameter"));
                groupNode.Nodes.Add(getNewNode("spawnY", "Parameter"));
                groupNode.Nodes.Add(getNewNode("spawnExplode", "Parameter"));
                spawnNode.Expand();
                groupNode.Expand();
            }
        }

        private void fixturesToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (treeView.SelectedNode != null && treeView.SelectedNode.Tag.Equals("State") && !treeView.SelectedNode.Nodes.Find("fixtures", true).Any())
            {
                treeView.SelectedNode.Expand();
                TreeNode spawnNode = getNewNode("fixtures", "Index");
                TreeNode groupNode = getNewNode("[1]", "Index");
                treeView.SelectedNode.Nodes.Add(spawnNode);
                spawnNode.Nodes.Add(groupNode);
                groupNode.Nodes.Add(getNewNode("x", "Parameter"));
                groupNode.Nodes.Add(getNewNode("y", "Parameter"));
                groupNode.Nodes.Add(getNewNode("w", "Parameter"));
                groupNode.Nodes.Add(getNewNode("h", "Parameter"));
                spawnNode.Expand();
                groupNode.Expand();
            }
        }

        private void aboutToolStripMenuItem_Click(object sender, EventArgs e)
        {
            using (var form = new frmAbout())
            {
                form.ShowDialog();
            }
        }
    }

    public static class Extensions
    {
        public static void MoveUp(this TreeNode node)
        {
            TreeNode parent = node.Parent;
            TreeView view = node.TreeView;
            if (parent != null)
            {
                int index = parent.Nodes.IndexOf(node);
                if (index > 0)
                {
                    parent.Nodes.RemoveAt(index);
                    parent.Nodes.Insert(index - 1, node);
                }
            }
            else if (node.TreeView.Nodes.Contains(node))
            {
                int index = view.Nodes.IndexOf(node);
                if (index > 0)
                {
                    view.Nodes.RemoveAt(index);
                    view.Nodes.Insert(index - 1, node);
                }
            }
        }

        public static void MoveDown(this TreeNode node)
        {
            TreeNode parent = node.Parent;
            TreeView view = node.TreeView;
            if (parent != null)
            {
                int index = parent.Nodes.IndexOf(node);
                if (index < parent.Nodes.Count - 1)
                {
                    parent.Nodes.RemoveAt(index);
                    parent.Nodes.Insert(index + 1, node);
                }
            }
            else if (view != null && view.Nodes.Contains(node))
            {
                int index = view.Nodes.IndexOf(node);
                if (index < view.Nodes.Count - 1)
                {
                    view.Nodes.RemoveAt(index);
                    view.Nodes.Insert(index + 1, node);
                }
            }
        }
    }

}