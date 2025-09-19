namespace Washmachine
{
    partial class MainForm
    {
        /// <summary>
        ///  Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        ///  Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        ///  Required method for Designer support - do not modify
        ///  the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(MainForm));
            label1 = new Label();
            shellcodeFile = new TextBox();
            button1 = new Button();
            label2 = new Label();
            shellcodeRAW = new TextBox();
            button2 = new Button();
            shellcode = new GroupBox();
            RAWShellcodeInfo = new Label();
            button3 = new Button();
            shellcodeURL = new TextBox();
            label11 = new Label();
            genericShellcodeComboBox = new ComboBox();
            label3 = new Label();
            groupBox1 = new GroupBox();
            guardRailsFormat = new Label();
            antiDebugListBox = new ListBox();
            PsInjPsNameTextBox = new TextBox();
            label9 = new Label();
            UACBComboBox = new ComboBox();
            label10 = new Label();
            shellcodeExecutionComboBox = new ComboBox();
            label8 = new Label();
            label7 = new Label();
            guardrailParamTextBox = new TextBox();
            guardrailComboBox = new ComboBox();
            label6 = new Label();
            psInjComboBox = new ComboBox();
            label5 = new Label();
            label4 = new Label();
            textBox4 = new TextBox();
            submitButton = new Button();
            debugBox = new RichTextBox();
            groupBox2 = new GroupBox();
            tabControl = new TabControl();
            MainTab = new TabPage();
            EncodingTab = new TabPage();
            bin2hexParametersGroup = new GroupBox();
            bin2hexEncoder = new ComboBox();
            bin2hexCompressor = new ComboBox();
            bin2hexEnvelope = new ComboBox();
            label15 = new Label();
            label13 = new Label();
            label14 = new Label();
            bin2hexCompLabel = new Label();
            label12 = new Label();
            PackingTab = new TabPage();
            BackdooringTab = new TabPage();
            shellcode.SuspendLayout();
            groupBox1.SuspendLayout();
            groupBox2.SuspendLayout();
            tabControl.SuspendLayout();
            MainTab.SuspendLayout();
            EncodingTab.SuspendLayout();
            bin2hexParametersGroup.SuspendLayout();
            SuspendLayout();
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Location = new Point(24, 29);
            label1.Name = "label1";
            label1.Size = new Size(158, 15);
            label1.TabIndex = 0;
            label1.Text = "Shellcode file (typically .bin):";
            // 
            // shellcodeFile
            // 
            shellcodeFile.Location = new Point(24, 46);
            shellcodeFile.Margin = new Padding(3, 2, 3, 2);
            shellcodeFile.Name = "shellcodeFile";
            shellcodeFile.Size = new Size(581, 23);
            shellcodeFile.TabIndex = 1;
            // 
            // button1
            // 
            button1.Location = new Point(609, 46);
            button1.Margin = new Padding(3, 2, 3, 2);
            button1.Name = "button1";
            button1.Size = new Size(70, 22);
            button1.TabIndex = 2;
            button1.Text = "Browse";
            button1.UseVisualStyleBackColor = true;
            button1.Click += button1_Click;
            // 
            // label2
            // 
            label2.AutoSize = true;
            label2.Location = new Point(24, 76);
            label2.Name = "label2";
            label2.Size = new Size(76, 15);
            label2.TabIndex = 3;
            label2.Text = "Or paste raw:";
            // 
            // shellcodeRAW
            // 
            shellcodeRAW.Font = new Font("Consolas", 9F, FontStyle.Regular, GraphicsUnit.Point, 0);
            shellcodeRAW.Location = new Point(24, 93);
            shellcodeRAW.Margin = new Padding(3, 2, 3, 2);
            shellcodeRAW.Multiline = true;
            shellcodeRAW.Name = "shellcodeRAW";
            shellcodeRAW.Size = new Size(581, 77);
            shellcodeRAW.TabIndex = 4;
            // 
            // button2
            // 
            button2.Location = new Point(609, 147);
            button2.Margin = new Padding(3, 2, 3, 2);
            button2.Name = "button2";
            button2.Size = new Size(70, 22);
            button2.TabIndex = 5;
            button2.Text = "Paste";
            button2.UseVisualStyleBackColor = true;
            button2.Click += button2_Click;
            // 
            // shellcode
            // 
            shellcode.Controls.Add(RAWShellcodeInfo);
            shellcode.Controls.Add(button3);
            shellcode.Controls.Add(shellcodeURL);
            shellcode.Controls.Add(label11);
            shellcode.Controls.Add(genericShellcodeComboBox);
            shellcode.Controls.Add(label3);
            shellcode.Controls.Add(button2);
            shellcode.Controls.Add(shellcodeRAW);
            shellcode.Controls.Add(label2);
            shellcode.Controls.Add(button1);
            shellcode.Controls.Add(shellcodeFile);
            shellcode.Controls.Add(label1);
            shellcode.Location = new Point(6, 16);
            shellcode.Margin = new Padding(3, 2, 3, 2);
            shellcode.Name = "shellcode";
            shellcode.Padding = new Padding(3, 2, 3, 2);
            shellcode.Size = new Size(697, 287);
            shellcode.TabIndex = 6;
            shellcode.TabStop = false;
            shellcode.Text = "Shellcode";
            // 
            // RAWShellcodeInfo
            // 
            RAWShellcodeInfo.AutoSize = true;
            RAWShellcodeInfo.Font = new Font("NSimSun", 9F, FontStyle.Underline, GraphicsUnit.Point, 0);
            RAWShellcodeInfo.ForeColor = SystemColors.HotTrack;
            RAWShellcodeInfo.Location = new Point(549, 80);
            RAWShellcodeInfo.Name = "RAWShellcodeInfo";
            RAWShellcodeInfo.Size = new Size(47, 12);
            RAWShellcodeInfo.TabIndex = 11;
            RAWShellcodeInfo.Text = "Format?";
            RAWShellcodeInfo.Click += RAWShellcodeInfo_Click;
            // 
            // button3
            // 
            button3.Location = new Point(609, 200);
            button3.Margin = new Padding(3, 2, 3, 2);
            button3.Name = "button3";
            button3.Size = new Size(70, 22);
            button3.TabIndex = 10;
            button3.Text = "Paste";
            button3.UseVisualStyleBackColor = true;
            button3.Click += button3_Click;
            // 
            // shellcodeURL
            // 
            shellcodeURL.Location = new Point(24, 200);
            shellcodeURL.Margin = new Padding(3, 2, 3, 2);
            shellcodeURL.Name = "shellcodeURL";
            shellcodeURL.Size = new Size(581, 23);
            shellcodeURL.TabIndex = 9;
            // 
            // label11
            // 
            label11.AutoSize = true;
            label11.Location = new Point(24, 183);
            label11.Name = "label11";
            label11.Size = new Size(85, 15);
            label11.TabIndex = 8;
            label11.Text = "Or from a URL:";
            // 
            // genericShellcodeComboBox
            // 
            genericShellcodeComboBox.FormattingEnabled = true;
            genericShellcodeComboBox.Location = new Point(24, 252);
            genericShellcodeComboBox.Margin = new Padding(3, 2, 3, 2);
            genericShellcodeComboBox.Name = "genericShellcodeComboBox";
            genericShellcodeComboBox.Size = new Size(302, 23);
            genericShellcodeComboBox.TabIndex = 7;
            // 
            // label3
            // 
            label3.AutoSize = true;
            label3.Location = new Point(24, 235);
            label3.Name = "label3";
            label3.Size = new Size(127, 15);
            label3.TabIndex = 6;
            label3.Text = "Or a generic shellcode:";
            // 
            // groupBox1
            // 
            groupBox1.Controls.Add(guardRailsFormat);
            groupBox1.Controls.Add(antiDebugListBox);
            groupBox1.Controls.Add(PsInjPsNameTextBox);
            groupBox1.Controls.Add(label9);
            groupBox1.Controls.Add(UACBComboBox);
            groupBox1.Controls.Add(label10);
            groupBox1.Controls.Add(shellcodeExecutionComboBox);
            groupBox1.Controls.Add(label8);
            groupBox1.Controls.Add(label7);
            groupBox1.Controls.Add(guardrailParamTextBox);
            groupBox1.Controls.Add(guardrailComboBox);
            groupBox1.Controls.Add(label6);
            groupBox1.Controls.Add(psInjComboBox);
            groupBox1.Controls.Add(label5);
            groupBox1.Controls.Add(label4);
            groupBox1.Location = new Point(6, 321);
            groupBox1.Margin = new Padding(3, 2, 3, 2);
            groupBox1.Name = "groupBox1";
            groupBox1.Padding = new Padding(3, 2, 3, 2);
            groupBox1.Size = new Size(697, 236);
            groupBox1.TabIndex = 7;
            groupBox1.TabStop = false;
            groupBox1.Text = "Parameters";
            // 
            // guardRailsFormat
            // 
            guardRailsFormat.AutoSize = true;
            guardRailsFormat.Font = new Font("NSimSun", 9F, FontStyle.Underline, GraphicsUnit.Point, 0);
            guardRailsFormat.ForeColor = SystemColors.HotTrack;
            guardRailsFormat.Location = new Point(632, 27);
            guardRailsFormat.Name = "guardRailsFormat";
            guardRailsFormat.Size = new Size(47, 12);
            guardRailsFormat.TabIndex = 12;
            guardRailsFormat.Text = "Format?";
            guardRailsFormat.Click += guardRailsFormat_Click;
            // 
            // antiDebugListBox
            // 
            antiDebugListBox.FormattingEnabled = true;
            antiDebugListBox.ItemHeight = 15;
            antiDebugListBox.Location = new Point(320, 140);
            antiDebugListBox.Margin = new Padding(3, 2, 3, 2);
            antiDebugListBox.Name = "antiDebugListBox";
            antiDebugListBox.SelectionMode = SelectionMode.MultiSimple;
            antiDebugListBox.Size = new Size(359, 79);
            antiDebugListBox.TabIndex = 15;
            // 
            // PsInjPsNameTextBox
            // 
            PsInjPsNameTextBox.Location = new Point(320, 90);
            PsInjPsNameTextBox.Margin = new Padding(3, 2, 3, 2);
            PsInjPsNameTextBox.Name = "PsInjPsNameTextBox";
            PsInjPsNameTextBox.Size = new Size(359, 23);
            PsInjPsNameTextBox.TabIndex = 14;
            // 
            // label9
            // 
            label9.AutoSize = true;
            label9.Location = new Point(320, 72);
            label9.Name = "label9";
            label9.Size = new Size(176, 15);
            label9.TabIndex = 13;
            label9.Text = "Process name (i.e. notepad.exe):";
            // 
            // UACBComboBox
            // 
            UACBComboBox.FormattingEnabled = true;
            UACBComboBox.Location = new Point(20, 192);
            UACBComboBox.Margin = new Padding(3, 2, 3, 2);
            UACBComboBox.Name = "UACBComboBox";
            UACBComboBox.Size = new Size(281, 23);
            UACBComboBox.TabIndex = 12;
            // 
            // label10
            // 
            label10.AutoSize = true;
            label10.Location = new Point(24, 175);
            label10.Name = "label10";
            label10.Size = new Size(73, 15);
            label10.TabIndex = 11;
            label10.Text = "UAC Bypass:";
            // 
            // shellcodeExecutionComboBox
            // 
            shellcodeExecutionComboBox.FormattingEnabled = true;
            shellcodeExecutionComboBox.Location = new Point(20, 140);
            shellcodeExecutionComboBox.Margin = new Padding(3, 2, 3, 2);
            shellcodeExecutionComboBox.Name = "shellcodeExecutionComboBox";
            shellcodeExecutionComboBox.Size = new Size(281, 23);
            shellcodeExecutionComboBox.TabIndex = 9;
            // 
            // label8
            // 
            label8.AutoSize = true;
            label8.Location = new Point(24, 122);
            label8.Name = "label8";
            label8.Size = new Size(115, 15);
            label8.TabIndex = 8;
            label8.Text = "Shellcode execution:";
            // 
            // label7
            // 
            label7.AutoSize = true;
            label7.Location = new Point(320, 23);
            label7.Name = "label7";
            label7.Size = new Size(77, 15);
            label7.TabIndex = 7;
            label7.Text = "Parameter(s):";
            // 
            // guardrailParamTextBox
            // 
            guardrailParamTextBox.Location = new Point(320, 40);
            guardrailParamTextBox.Margin = new Padding(3, 2, 3, 2);
            guardrailParamTextBox.Name = "guardrailParamTextBox";
            guardrailParamTextBox.Size = new Size(359, 23);
            guardrailParamTextBox.TabIndex = 6;
            // 
            // guardrailComboBox
            // 
            guardrailComboBox.FormattingEnabled = true;
            guardrailComboBox.Location = new Point(20, 40);
            guardrailComboBox.Margin = new Padding(3, 2, 3, 2);
            guardrailComboBox.Name = "guardrailComboBox";
            guardrailComboBox.Size = new Size(281, 23);
            guardrailComboBox.TabIndex = 5;
            // 
            // label6
            // 
            label6.AutoSize = true;
            label6.Location = new Point(23, 23);
            label6.Name = "label6";
            label6.Size = new Size(125, 15);
            label6.TabIndex = 4;
            label6.Text = "Guardrail(s) (env vars):";
            // 
            // psInjComboBox
            // 
            psInjComboBox.FormattingEnabled = true;
            psInjComboBox.Location = new Point(20, 89);
            psInjComboBox.Margin = new Padding(3, 2, 3, 2);
            psInjComboBox.Name = "psInjComboBox";
            psInjComboBox.Size = new Size(281, 23);
            psInjComboBox.TabIndex = 3;
            // 
            // label5
            // 
            label5.AutoSize = true;
            label5.Location = new Point(23, 72);
            label5.Name = "label5";
            label5.Size = new Size(99, 15);
            label5.TabIndex = 2;
            label5.Text = "Process Injection:";
            // 
            // label4
            // 
            label4.AutoSize = true;
            label4.Location = new Point(320, 122);
            label4.Name = "label4";
            label4.Size = new Size(221, 15);
            label4.TabIndex = 0;
            label4.Text = "Anti Debug (Multiple selection possible):";
            // 
            // textBox4
            // 
            textBox4.BackColor = SystemColors.Menu;
            textBox4.BorderStyle = BorderStyle.None;
            textBox4.Enabled = false;
            textBox4.ForeColor = Color.Brown;
            textBox4.Location = new Point(748, 609);
            textBox4.Margin = new Padding(3, 2, 3, 2);
            textBox4.Multiline = true;
            textBox4.Name = "textBox4";
            textBox4.Size = new Size(588, 62);
            textBox4.TabIndex = 8;
            textBox4.Text = resources.GetString("textBox4.Text");
            // 
            // submitButton
            // 
            submitButton.Location = new Point(311, 609);
            submitButton.Margin = new Padding(3, 2, 3, 2);
            submitButton.Name = "submitButton";
            submitButton.Size = new Size(108, 22);
            submitButton.TabIndex = 9;
            submitButton.Text = "Compile";
            submitButton.UseVisualStyleBackColor = true;
            submitButton.Click += submitButton_Click;
            // 
            // debugBox
            // 
            debugBox.DetectUrls = false;
            debugBox.Location = new Point(10, 22);
            debugBox.Name = "debugBox";
            debugBox.ReadOnly = true;
            debugBox.Size = new Size(575, 559);
            debugBox.TabIndex = 10;
            debugBox.Text = "";
            debugBox.WordWrap = false;
            // 
            // groupBox2
            // 
            groupBox2.Controls.Add(debugBox);
            groupBox2.Location = new Point(738, 9);
            groupBox2.Name = "groupBox2";
            groupBox2.Size = new Size(598, 591);
            groupBox2.TabIndex = 11;
            groupBox2.TabStop = false;
            groupBox2.Text = "Logs";
            // 
            // tabControl
            // 
            tabControl.Controls.Add(MainTab);
            tabControl.Controls.Add(EncodingTab);
            tabControl.Controls.Add(PackingTab);
            tabControl.Controls.Add(BackdooringTab);
            tabControl.Location = new Point(12, 9);
            tabControl.Name = "tabControl";
            tabControl.SelectedIndex = 0;
            tabControl.Size = new Size(720, 595);
            tabControl.TabIndex = 12;
            // 
            // MainTab
            // 
            MainTab.Controls.Add(shellcode);
            MainTab.Controls.Add(groupBox1);
            MainTab.Location = new Point(4, 24);
            MainTab.Name = "MainTab";
            MainTab.Padding = new Padding(3);
            MainTab.Size = new Size(712, 567);
            MainTab.TabIndex = 0;
            MainTab.Text = "Main";
            MainTab.UseVisualStyleBackColor = true;
            // 
            // EncodingTab
            // 
            EncodingTab.Controls.Add(bin2hexParametersGroup);
            EncodingTab.Location = new Point(4, 24);
            EncodingTab.Name = "EncodingTab";
            EncodingTab.Padding = new Padding(3);
            EncodingTab.Size = new Size(712, 567);
            EncodingTab.TabIndex = 1;
            EncodingTab.Text = "Shellcode Encoding";
            EncodingTab.UseVisualStyleBackColor = true;
            // 
            // bin2hexParametersGroup
            // 
            bin2hexParametersGroup.Controls.Add(bin2hexEncoder);
            bin2hexParametersGroup.Controls.Add(bin2hexCompressor);
            bin2hexParametersGroup.Controls.Add(bin2hexEnvelope);
            bin2hexParametersGroup.Controls.Add(label15);
            bin2hexParametersGroup.Controls.Add(label13);
            bin2hexParametersGroup.Controls.Add(label14);
            bin2hexParametersGroup.Controls.Add(bin2hexCompLabel);
            bin2hexParametersGroup.Controls.Add(label12);
            bin2hexParametersGroup.Location = new Point(6, 6);
            bin2hexParametersGroup.Name = "bin2hexParametersGroup";
            bin2hexParametersGroup.Size = new Size(700, 99);
            bin2hexParametersGroup.TabIndex = 0;
            bin2hexParametersGroup.TabStop = false;
            bin2hexParametersGroup.Text = "Bin2hex.py parameters";
            // 
            // bin2hexEncoder
            // 
            bin2hexEncoder.FormattingEnabled = true;
            bin2hexEncoder.Location = new Point(35, 56);
            bin2hexEncoder.Name = "bin2hexEncoder";
            bin2hexEncoder.Size = new Size(183, 23);
            bin2hexEncoder.TabIndex = 6;
            // 
            // bin2hexCompressor
            // 
            bin2hexCompressor.FormattingEnabled = true;
            bin2hexCompressor.Location = new Point(250, 56);
            bin2hexCompressor.Name = "bin2hexCompressor";
            bin2hexCompressor.Size = new Size(183, 23);
            bin2hexCompressor.TabIndex = 5;
            // 
            // bin2hexEnvelope
            // 
            bin2hexEnvelope.FormattingEnabled = true;
            bin2hexEnvelope.Location = new Point(466, 56);
            bin2hexEnvelope.Name = "bin2hexEnvelope";
            bin2hexEnvelope.Size = new Size(183, 23);
            bin2hexEnvelope.TabIndex = 4;
            // 
            // label15
            // 
            label15.AutoSize = true;
            label15.Location = new Point(440, 28);
            label15.Name = "label15";
            label15.Size = new Size(20, 15);
            label15.TabIndex = 3;
            label15.Text = "->";
            // 
            // label13
            // 
            label13.AutoSize = true;
            label13.Location = new Point(223, 28);
            label13.Name = "label13";
            label13.Size = new Size(20, 15);
            label13.TabIndex = 1;
            label13.Text = "->";
            // 
            // label14
            // 
            label14.AutoSize = true;
            label14.Location = new Point(530, 28);
            label14.Name = "label14";
            label14.Size = new Size(55, 15);
            label14.TabIndex = 2;
            label14.Text = "Envelope";
            // 
            // bin2hexCompLabel
            // 
            bin2hexCompLabel.AutoSize = true;
            bin2hexCompLabel.Location = new Point(306, 28);
            bin2hexCompLabel.Name = "bin2hexCompLabel";
            bin2hexCompLabel.Size = new Size(71, 15);
            bin2hexCompLabel.TabIndex = 1;
            bin2hexCompLabel.Text = "Compressor";
            // 
            // label12
            // 
            label12.AutoSize = true;
            label12.Location = new Point(101, 28);
            label12.Name = "label12";
            label12.Size = new Size(50, 15);
            label12.TabIndex = 0;
            label12.Text = "Encoder";
            // 
            // PackingTab
            // 
            PackingTab.Location = new Point(4, 24);
            PackingTab.Name = "PackingTab";
            PackingTab.Size = new Size(712, 567);
            PackingTab.TabIndex = 2;
            PackingTab.Text = "Packing";
            PackingTab.UseVisualStyleBackColor = true;
            // 
            // BackdooringTab
            // 
            BackdooringTab.Location = new Point(4, 24);
            BackdooringTab.Name = "BackdooringTab";
            BackdooringTab.Size = new Size(712, 567);
            BackdooringTab.TabIndex = 3;
            BackdooringTab.Text = "Backdooring";
            BackdooringTab.UseVisualStyleBackColor = true;
            // 
            // MainForm
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = SystemColors.Control;
            ClientSize = new Size(1348, 682);
            Controls.Add(tabControl);
            Controls.Add(groupBox2);
            Controls.Add(submitButton);
            Controls.Add(textBox4);
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            Margin = new Padding(3, 2, 3, 2);
            MaximizeBox = false;
            Name = "MainForm";
            ShowIcon = false;
            StartPosition = FormStartPosition.CenterScreen;
            Text = "Washmachine - Loader Builder";
            Load += MainForm_Load;
            shellcode.ResumeLayout(false);
            shellcode.PerformLayout();
            groupBox1.ResumeLayout(false);
            groupBox1.PerformLayout();
            groupBox2.ResumeLayout(false);
            tabControl.ResumeLayout(false);
            MainTab.ResumeLayout(false);
            EncodingTab.ResumeLayout(false);
            bin2hexParametersGroup.ResumeLayout(false);
            bin2hexParametersGroup.PerformLayout();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private Label label1;
        private TextBox shellcodeFile;
        private Button button1;
        private Label label2;
        private TextBox shellcodeRAW;
        private Button button2;
        private GroupBox shellcode;
        private ComboBox genericShellcodeComboBox;
        private Label label3;
        private GroupBox groupBox1;
        private Label label5;
        private Label label4;
        private Label label7;
        private TextBox guardrailParamTextBox;
        private ComboBox guardrailComboBox;
        private Label label6;
        private ComboBox psInjComboBox;
        private ComboBox shellcodeExecutionComboBox;
        private Label label8;
        private ComboBox UACBComboBox;
        private Label label10;
        private TextBox textBox4;
        private TextBox shellcodeURL;
        private Label label11;
        private Button button3;
        private TextBox PsInjPsNameTextBox;
        private Label label9;
        private Label RAWShellcodeInfo;
        private ListBox antiDebugListBox;
        private Label guardRailsFormat;
        private Button submitButton;
        private RichTextBox debugBox;
        private GroupBox groupBox2;
        private TabControl tabControl;
        private TabPage MainTab;
        private TabPage EncodingTab;
        private TabPage PackingTab;
        private TabPage BackdooringTab;
        private GroupBox bin2hexParametersGroup;
        private ComboBox comboBox5;
        private ComboBox comboBox4;
        private ComboBox bin2hexEnvelope;
        private Label label15;
        private Label label13;
        private Label label14;
        private Label bin2hexCompLabel;
        private Label label12;
        private ComboBox bin2hexEncoder;
        private ComboBox bin2hexCompressor;
    }
}
