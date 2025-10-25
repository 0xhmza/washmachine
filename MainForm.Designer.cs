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
            templateLabel = new Label();
            templateComboBox = new ComboBox();
            textBox4 = new TextBox();
            submitButton = new Button();
            debugBox = new RichTextBox();
            groupBox2 = new GroupBox();
            tabControl = new TabControl();
            MainTab = new TabPage();
            EncodingTab = new TabPage();
            bin2hexParametersGroup = new GroupBox();
            label16 = new Label();
            bin2shellOptions = new ComboBox();
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
            SnippetsPicker = new FlowLayoutPanel();
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
            label1.Location = new Point(34, 49);
            label1.Margin = new Padding(4, 0, 4, 0);
            label1.Name = "label1";
            label1.Size = new Size(233, 25);
            label1.TabIndex = 0;
            label1.Text = "Shellcode file (typically .bin):";
            // 
            // shellcodeFile
            // 
            shellcodeFile.Location = new Point(34, 76);
            shellcodeFile.Margin = new Padding(4, 4, 4, 4);
            shellcodeFile.Name = "shellcodeFile";
            shellcodeFile.Size = new Size(828, 31);
            shellcodeFile.TabIndex = 1;
            // 
            // button1
            // 
            button1.Location = new Point(870, 76);
            button1.Margin = new Padding(4, 4, 4, 4);
            button1.Name = "button1";
            button1.Size = new Size(100, 36);
            button1.TabIndex = 2;
            button1.Text = "Browse";
            button1.UseVisualStyleBackColor = true;
            button1.Click += button1_Click;
            // 
            // label2
            // 
            label2.AutoSize = true;
            label2.Location = new Point(34, 126);
            label2.Margin = new Padding(4, 0, 4, 0);
            label2.Name = "label2";
            label2.Size = new Size(117, 25);
            label2.TabIndex = 3;
            label2.Text = "Or paste raw:";
            // 
            // shellcodeRAW
            // 
            shellcodeRAW.Font = new Font("Consolas", 9F, FontStyle.Regular, GraphicsUnit.Point, 0);
            shellcodeRAW.Location = new Point(34, 155);
            shellcodeRAW.Margin = new Padding(4, 4, 4, 4);
            shellcodeRAW.Multiline = true;
            shellcodeRAW.Name = "shellcodeRAW";
            shellcodeRAW.Size = new Size(828, 125);
            shellcodeRAW.TabIndex = 4;
            // 
            // button2
            // 
            button2.Location = new Point(870, 245);
            button2.Margin = new Padding(4, 4, 4, 4);
            button2.Name = "button2";
            button2.Size = new Size(100, 36);
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
            shellcode.Location = new Point(9, 26);
            shellcode.Margin = new Padding(4, 4, 4, 4);
            shellcode.Name = "shellcode";
            shellcode.Padding = new Padding(4, 4, 4, 4);
            shellcode.Size = new Size(996, 479);
            shellcode.TabIndex = 6;
            shellcode.TabStop = false;
            shellcode.Text = "Shellcode";
            // 
            // RAWShellcodeInfo
            // 
            RAWShellcodeInfo.AutoSize = true;
            RAWShellcodeInfo.Font = new Font("NSimSun", 9F, FontStyle.Underline, GraphicsUnit.Point, 0);
            RAWShellcodeInfo.ForeColor = SystemColors.HotTrack;
            RAWShellcodeInfo.Location = new Point(784, 134);
            RAWShellcodeInfo.Margin = new Padding(4, 0, 4, 0);
            RAWShellcodeInfo.Name = "RAWShellcodeInfo";
            RAWShellcodeInfo.Size = new Size(71, 18);
            RAWShellcodeInfo.TabIndex = 11;
            RAWShellcodeInfo.Text = "Format?";
            RAWShellcodeInfo.Click += RAWShellcodeInfo_Click;
            // 
            // button3
            // 
            button3.Location = new Point(870, 334);
            button3.Margin = new Padding(4, 4, 4, 4);
            button3.Name = "button3";
            button3.Size = new Size(100, 36);
            button3.TabIndex = 10;
            button3.Text = "Paste";
            button3.UseVisualStyleBackColor = true;
            button3.Click += button3_Click;
            // 
            // shellcodeURL
            // 
            shellcodeURL.Location = new Point(34, 334);
            shellcodeURL.Margin = new Padding(4, 4, 4, 4);
            shellcodeURL.Name = "shellcodeURL";
            shellcodeURL.Size = new Size(828, 31);
            shellcodeURL.TabIndex = 9;
            // 
            // label11
            // 
            label11.AutoSize = true;
            label11.Location = new Point(34, 305);
            label11.Margin = new Padding(4, 0, 4, 0);
            label11.Name = "label11";
            label11.Size = new Size(130, 25);
            label11.TabIndex = 8;
            label11.Text = "Or from a URL:";
            // 
            // genericShellcodeComboBox
            // 
            genericShellcodeComboBox.FormattingEnabled = true;
            genericShellcodeComboBox.Location = new Point(34, 420);
            genericShellcodeComboBox.Margin = new Padding(4, 4, 4, 4);
            genericShellcodeComboBox.Name = "genericShellcodeComboBox";
            genericShellcodeComboBox.Size = new Size(430, 33);
            genericShellcodeComboBox.TabIndex = 7;
            // 
            // label3
            // 
            label3.AutoSize = true;
            label3.Location = new Point(34, 391);
            label3.Margin = new Padding(4, 0, 4, 0);
            label3.Name = "label3";
            label3.Size = new Size(191, 25);
            label3.TabIndex = 6;
            label3.Text = "Or a generic shellcode:";
            // 
            // templateLabel
            // 
            templateLabel.AutoSize = true;
            templateLabel.Location = new Point(12, 35);
            templateLabel.Margin = new Padding(4, 0, 4, 0);
            templateLabel.Name = "templateLabel";
            templateLabel.Size = new Size(131, 25);
            templateLabel.TabIndex = 0;
            templateLabel.Text = "Code template:";
            // 
            // templateComboBox
            // 
            templateComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            templateComboBox.FormattingEnabled = true;
            templateComboBox.Location = new Point(152, 31);
            templateComboBox.Margin = new Padding(4, 4, 4, 4);
            templateComboBox.Name = "templateComboBox";
            templateComboBox.Size = new Size(430, 33);
            templateComboBox.TabIndex = 1;
            templateComboBox.SelectedIndexChanged += templateComboBox_SelectedIndexChanged;
            // 
            // groupBox1
            // 
            groupBox1.Controls.Add(SnippetsPicker);
            groupBox1.Controls.Add(templateComboBox);
            groupBox1.Controls.Add(templateLabel);
            groupBox1.Location = new Point(9, 535);
            groupBox1.Margin = new Padding(4, 4, 4, 4);
            groupBox1.Name = "groupBox1";
            groupBox1.Padding = new Padding(4, 4, 4, 4);
            groupBox1.Size = new Size(996, 394);
            groupBox1.TabIndex = 7;
            groupBox1.TabStop = false;
            groupBox1.Text = "Parameters";
            // 
            // textBox4
            // 
            textBox4.BackColor = SystemColors.Menu;
            textBox4.BorderStyle = BorderStyle.None;
            textBox4.Enabled = false;
            textBox4.ForeColor = Color.Brown;
            textBox4.Location = new Point(1069, 1015);
            textBox4.Margin = new Padding(4, 4, 4, 4);
            textBox4.Multiline = true;
            textBox4.Name = "textBox4";
            textBox4.Size = new Size(840, 104);
            textBox4.TabIndex = 8;
            textBox4.Text = resources.GetString("textBox4.Text");
            // 
            // submitButton
            // 
            submitButton.Location = new Point(444, 1015);
            submitButton.Margin = new Padding(4, 4, 4, 4);
            submitButton.Name = "submitButton";
            submitButton.Size = new Size(154, 36);
            submitButton.TabIndex = 9;
            submitButton.Text = "Compile";
            submitButton.UseVisualStyleBackColor = true;
            submitButton.Click += submitButton_Click;
            // 
            // debugBox
            // 
            debugBox.DetectUrls = false;
            debugBox.Location = new Point(14, 36);
            debugBox.Margin = new Padding(4, 5, 4, 5);
            debugBox.Name = "debugBox";
            debugBox.ReadOnly = true;
            debugBox.Size = new Size(820, 929);
            debugBox.TabIndex = 10;
            debugBox.Text = "";
            debugBox.WordWrap = false;
            // 
            // groupBox2
            // 
            groupBox2.Controls.Add(debugBox);
            groupBox2.Location = new Point(1054, 15);
            groupBox2.Margin = new Padding(4, 5, 4, 5);
            groupBox2.Name = "groupBox2";
            groupBox2.Padding = new Padding(4, 5, 4, 5);
            groupBox2.Size = new Size(854, 985);
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
            tabControl.Location = new Point(18, 15);
            tabControl.Margin = new Padding(4, 5, 4, 5);
            tabControl.Name = "tabControl";
            tabControl.SelectedIndex = 0;
            tabControl.Size = new Size(1029, 991);
            tabControl.TabIndex = 12;
            // 
            // MainTab
            // 
            MainTab.Controls.Add(shellcode);
            MainTab.Controls.Add(groupBox1);
            MainTab.Location = new Point(4, 34);
            MainTab.Margin = new Padding(4, 5, 4, 5);
            MainTab.Name = "MainTab";
            MainTab.Padding = new Padding(4, 5, 4, 5);
            MainTab.Size = new Size(1021, 953);
            MainTab.TabIndex = 0;
            MainTab.Text = "Main";
            MainTab.UseVisualStyleBackColor = true;
            // 
            // EncodingTab
            // 
            EncodingTab.Controls.Add(bin2hexParametersGroup);
            EncodingTab.Location = new Point(4, 34);
            EncodingTab.Margin = new Padding(4, 5, 4, 5);
            EncodingTab.Name = "EncodingTab";
            EncodingTab.Padding = new Padding(4, 5, 4, 5);
            EncodingTab.Size = new Size(1021, 953);
            EncodingTab.TabIndex = 1;
            EncodingTab.Text = "Shellcode Encoding";
            EncodingTab.UseVisualStyleBackColor = true;
            // 
            // bin2hexParametersGroup
            // 
            bin2hexParametersGroup.Controls.Add(label16);
            bin2hexParametersGroup.Controls.Add(bin2shellOptions);
            bin2hexParametersGroup.Controls.Add(bin2hexEncoder);
            bin2hexParametersGroup.Controls.Add(bin2hexCompressor);
            bin2hexParametersGroup.Controls.Add(bin2hexEnvelope);
            bin2hexParametersGroup.Controls.Add(label15);
            bin2hexParametersGroup.Controls.Add(label13);
            bin2hexParametersGroup.Controls.Add(label14);
            bin2hexParametersGroup.Controls.Add(bin2hexCompLabel);
            bin2hexParametersGroup.Controls.Add(label12);
            bin2hexParametersGroup.Location = new Point(9, 10);
            bin2hexParametersGroup.Margin = new Padding(4, 5, 4, 5);
            bin2hexParametersGroup.Name = "bin2hexParametersGroup";
            bin2hexParametersGroup.Padding = new Padding(4, 5, 4, 5);
            bin2hexParametersGroup.Size = new Size(1000, 275);
            bin2hexParametersGroup.TabIndex = 0;
            bin2hexParametersGroup.TabStop = false;
            bin2hexParametersGroup.Text = "Bin2hex.py parameters";
            // 
            // label16
            // 
            label16.AutoSize = true;
            label16.Location = new Point(144, 162);
            label16.Margin = new Padding(4, 0, 4, 0);
            label16.Name = "label16";
            label16.Size = new Size(76, 25);
            label16.TabIndex = 8;
            label16.Text = "Options";
            // 
            // bin2shellOptions
            // 
            bin2shellOptions.FormattingEnabled = true;
            bin2shellOptions.Location = new Point(50, 204);
            bin2shellOptions.Margin = new Padding(4, 4, 4, 4);
            bin2shellOptions.Name = "bin2shellOptions";
            bin2shellOptions.Size = new Size(260, 33);
            bin2shellOptions.TabIndex = 7;
            // 
            // bin2hexEncoder
            // 
            bin2hexEncoder.FormattingEnabled = true;
            bin2hexEncoder.Location = new Point(50, 94);
            bin2hexEncoder.Margin = new Padding(4, 5, 4, 5);
            bin2hexEncoder.Name = "bin2hexEncoder";
            bin2hexEncoder.Size = new Size(260, 33);
            bin2hexEncoder.TabIndex = 6;
            // 
            // bin2hexCompressor
            // 
            bin2hexCompressor.FormattingEnabled = true;
            bin2hexCompressor.Location = new Point(358, 94);
            bin2hexCompressor.Margin = new Padding(4, 5, 4, 5);
            bin2hexCompressor.Name = "bin2hexCompressor";
            bin2hexCompressor.Size = new Size(260, 33);
            bin2hexCompressor.TabIndex = 5;
            // 
            // bin2hexEnvelope
            // 
            bin2hexEnvelope.FormattingEnabled = true;
            bin2hexEnvelope.Location = new Point(666, 94);
            bin2hexEnvelope.Margin = new Padding(4, 5, 4, 5);
            bin2hexEnvelope.Name = "bin2hexEnvelope";
            bin2hexEnvelope.Size = new Size(260, 33);
            bin2hexEnvelope.TabIndex = 4;
            // 
            // label15
            // 
            label15.AutoSize = true;
            label15.Location = new Point(629, 46);
            label15.Margin = new Padding(4, 0, 4, 0);
            label15.Name = "label15";
            label15.Size = new Size(31, 25);
            label15.TabIndex = 3;
            label15.Text = "->";
            // 
            // label13
            // 
            label13.AutoSize = true;
            label13.Location = new Point(319, 46);
            label13.Margin = new Padding(4, 0, 4, 0);
            label13.Name = "label13";
            label13.Size = new Size(31, 25);
            label13.TabIndex = 1;
            label13.Text = "->";
            // 
            // label14
            // 
            label14.AutoSize = true;
            label14.Location = new Point(758, 46);
            label14.Margin = new Padding(4, 0, 4, 0);
            label14.Name = "label14";
            label14.Size = new Size(84, 25);
            label14.TabIndex = 2;
            label14.Text = "Envelope";
            // 
            // bin2hexCompLabel
            // 
            bin2hexCompLabel.AutoSize = true;
            bin2hexCompLabel.Location = new Point(438, 46);
            bin2hexCompLabel.Margin = new Padding(4, 0, 4, 0);
            bin2hexCompLabel.Name = "bin2hexCompLabel";
            bin2hexCompLabel.Size = new Size(109, 25);
            bin2hexCompLabel.TabIndex = 1;
            bin2hexCompLabel.Text = "Compressor";
            // 
            // label12
            // 
            label12.AutoSize = true;
            label12.Location = new Point(144, 46);
            label12.Margin = new Padding(4, 0, 4, 0);
            label12.Name = "label12";
            label12.Size = new Size(76, 25);
            label12.TabIndex = 0;
            label12.Text = "Encoder";
            // 
            // PackingTab
            // 
            PackingTab.Location = new Point(4, 34);
            PackingTab.Margin = new Padding(4, 5, 4, 5);
            PackingTab.Name = "PackingTab";
            PackingTab.Size = new Size(1021, 953);
            PackingTab.TabIndex = 2;
            PackingTab.Text = "Packing";
            PackingTab.UseVisualStyleBackColor = true;
            // 
            // BackdooringTab
            // 
            BackdooringTab.Location = new Point(4, 34);
            BackdooringTab.Margin = new Padding(4, 5, 4, 5);
            BackdooringTab.Name = "BackdooringTab";
            BackdooringTab.Size = new Size(1021, 953);
            BackdooringTab.TabIndex = 3;
            BackdooringTab.Text = "Backdooring";
            BackdooringTab.UseVisualStyleBackColor = true;
            // 
            // SnippetsPicker
            // 
            SnippetsPicker.AutoScroll = true;
            SnippetsPicker.Location = new Point(7, 80);
            SnippetsPicker.Name = "SnippetsPicker";
            SnippetsPicker.Size = new Size(982, 306);
            SnippetsPicker.TabIndex = 2;
            // 
            // MainForm
            // 
            AutoScaleDimensions = new SizeF(10F, 25F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = SystemColors.Control;
            ClientSize = new Size(1926, 1136);
            Controls.Add(tabControl);
            Controls.Add(groupBox2);
            Controls.Add(submitButton);
            Controls.Add(textBox4);
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            Margin = new Padding(4, 4, 4, 4);
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
        private Label templateLabel;
        private ComboBox templateComboBox;
        private TextBox textBox4;
        private TextBox shellcodeURL;
        private Label label11;
        private Button button3;
        private Label RAWShellcodeInfo;
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
        private Label label16;
        private ComboBox bin2shellOptions;
        private FlowLayoutPanel SnippetsPicker;
    }
}
