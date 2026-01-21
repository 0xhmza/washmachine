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
            textBox4 = new TextBox();
            debugBox = new RichTextBox();
            groupBox2 = new GroupBox();
            BackdooringTab = new TabPage();
            PackingTab = new TabPage();
            MainTab = new TabPage();
            submitButton = new Button();
            groupBox3 = new GroupBox();
            bin2hexEncoder = new ComboBox();
            bin2hexEnvelope = new ComboBox();
            payloadEncodingEnvelopeLabel = new Label();
            payloadEncodingEncoderLabel = new Label();
            shellcode = new GroupBox();
            shellcodeType = new TabControl();
            fileShellcodeTab = new TabPage();
            WebPayloadGenerator = new Button();
            button1 = new Button();
            shellcodeFile = new TextBox();
            label1 = new Label();
            RawShellcodeTab = new TabPage();
            RAWShellcodeInfo = new Label();
            button2 = new Button();
            label2 = new Label();
            shellcodeRAW = new TextBox();
            URLShellcodeTab = new TabPage();
            label4 = new Label();
            button3 = new Button();
            shellcodeURL = new TextBox();
            label11 = new Label();
            genericShellcodeTab = new TabPage();
            genericShellcodeComboBox = new ComboBox();
            label3 = new Label();
            groupBox1 = new GroupBox();
            button4 = new Button();
            templateComboBox = new ComboBox();
            tabControl = new TabControl();
            groupBox2.SuspendLayout();
            MainTab.SuspendLayout();
            groupBox3.SuspendLayout();
            shellcode.SuspendLayout();
            shellcodeType.SuspendLayout();
            fileShellcodeTab.SuspendLayout();
            RawShellcodeTab.SuspendLayout();
            URLShellcodeTab.SuspendLayout();
            genericShellcodeTab.SuspendLayout();
            groupBox1.SuspendLayout();
            tabControl.SuspendLayout();
            SuspendLayout();
            // 
            // textBox4
            // 
            textBox4.BackColor = SystemColors.Control;
            textBox4.BorderStyle = BorderStyle.None;
            textBox4.Enabled = false;
            textBox4.ForeColor = Color.Brown;
            textBox4.Location = new Point(3, 577);
            textBox4.Multiline = true;
            textBox4.Name = "textBox4";
            textBox4.Size = new Size(770, 109);
            textBox4.TabIndex = 8;
            textBox4.Text = resources.GetString("textBox4.Text");
            // 
            // debugBox
            // 
            debugBox.DetectUrls = false;
            debugBox.Location = new Point(6, 27);
            debugBox.Margin = new Padding(3, 4, 3, 4);
            debugBox.Name = "debugBox";
            debugBox.ReadOnly = true;
            debugBox.Size = new Size(759, 121);
            debugBox.TabIndex = 10;
            debugBox.Text = "";
            debugBox.WordWrap = false;
            // 
            // groupBox2
            // 
            groupBox2.Controls.Add(debugBox);
            groupBox2.Location = new Point(3, 411);
            groupBox2.Margin = new Padding(3, 4, 3, 4);
            groupBox2.Name = "groupBox2";
            groupBox2.Padding = new Padding(3, 4, 3, 4);
            groupBox2.Size = new Size(773, 159);
            groupBox2.TabIndex = 11;
            groupBox2.TabStop = false;
            groupBox2.Text = "Logs";
            // 
            // BackdooringTab
            // 
            BackdooringTab.Location = new Point(4, 29);
            BackdooringTab.Margin = new Padding(3, 4, 3, 4);
            BackdooringTab.Name = "BackdooringTab";
            BackdooringTab.Size = new Size(786, 692);
            BackdooringTab.TabIndex = 3;
            BackdooringTab.Text = "Backdooring";
            BackdooringTab.UseVisualStyleBackColor = true;
            // 
            // PackingTab
            // 
            PackingTab.Location = new Point(4, 29);
            PackingTab.Margin = new Padding(3, 4, 3, 4);
            PackingTab.Name = "PackingTab";
            PackingTab.Size = new Size(786, 692);
            PackingTab.TabIndex = 2;
            PackingTab.Text = "Packing";
            PackingTab.UseVisualStyleBackColor = true;
            // 
            // MainTab
            // 
            MainTab.AutoScroll = true;
            MainTab.BackColor = Color.Transparent;
            MainTab.Controls.Add(textBox4);
            MainTab.Controls.Add(groupBox2);
            MainTab.Controls.Add(groupBox3);
            MainTab.Controls.Add(shellcode);
            MainTab.Controls.Add(groupBox1);
            MainTab.Controls.Add(submitButton);
            MainTab.Location = new Point(4, 29);
            MainTab.Margin = new Padding(3, 4, 3, 4);
            MainTab.Name = "MainTab";
            MainTab.Padding = new Padding(3, 4, 3, 4);
            MainTab.Size = new Size(794, 692);
            MainTab.TabIndex = 0;
            MainTab.Text = "Main";
            // 
            // submitButton
            // 
            submitButton.Location = new Point(354, 375);
            submitButton.Name = "submitButton";
            submitButton.Size = new Size(123, 29);
            submitButton.TabIndex = 9;
            submitButton.Text = "Compile";
            submitButton.UseVisualStyleBackColor = true;
            submitButton.Click += submitButton_Click;
            // 
            // groupBox3
            // 
            groupBox3.Controls.Add(bin2hexEncoder);
            groupBox3.Controls.Add(bin2hexEnvelope);
            groupBox3.Controls.Add(payloadEncodingEnvelopeLabel);
            groupBox3.Controls.Add(payloadEncodingEncoderLabel);
            groupBox3.Location = new Point(3, 207);
            groupBox3.Margin = new Padding(2);
            groupBox3.Name = "groupBox3";
            groupBox3.Padding = new Padding(2);
            groupBox3.Size = new Size(776, 86);
            groupBox3.TabIndex = 10;
            groupBox3.TabStop = false;
            groupBox3.Text = "Payload Encoding";
            // 
            // bin2hexEncoder
            // 
            bin2hexEncoder.FormattingEnabled = true;
            bin2hexEncoder.Location = new Point(98, 36);
            bin2hexEncoder.Margin = new Padding(3, 4, 3, 4);
            bin2hexEncoder.Name = "bin2hexEncoder";
            bin2hexEncoder.Size = new Size(274, 28);
            bin2hexEncoder.TabIndex = 8;
            // 
            // bin2hexEnvelope
            // 
            bin2hexEnvelope.FormattingEnabled = true;
            bin2hexEnvelope.Location = new Point(480, 36);
            bin2hexEnvelope.Margin = new Padding(3, 4, 3, 4);
            bin2hexEnvelope.Name = "bin2hexEnvelope";
            bin2hexEnvelope.Size = new Size(274, 28);
            bin2hexEnvelope.TabIndex = 7;
            // 
            // payloadEncodingEnvelopeLabel
            // 
            payloadEncodingEnvelopeLabel.AutoSize = true;
            payloadEncodingEnvelopeLabel.Location = new Point(401, 39);
            payloadEncodingEnvelopeLabel.Name = "payloadEncodingEnvelopeLabel";
            payloadEncodingEnvelopeLabel.Size = new Size(73, 20);
            payloadEncodingEnvelopeLabel.TabIndex = 4;
            payloadEncodingEnvelopeLabel.Text = "Envelope:";
            // 
            // payloadEncodingEncoderLabel
            // 
            payloadEncodingEncoderLabel.AutoSize = true;
            payloadEncodingEncoderLabel.Location = new Point(27, 36);
            payloadEncodingEncoderLabel.Name = "payloadEncodingEncoderLabel";
            payloadEncodingEncoderLabel.Size = new Size(66, 20);
            payloadEncodingEncoderLabel.TabIndex = 3;
            payloadEncodingEncoderLabel.Text = "Encoder:";
            // 
            // shellcode
            // 
            shellcode.Controls.Add(shellcodeType);
            shellcode.Location = new Point(3, 15);
            shellcode.Name = "shellcode";
            shellcode.Size = new Size(776, 187);
            shellcode.TabIndex = 6;
            shellcode.TabStop = false;
            shellcode.Text = "Shellcode";
            // 
            // shellcodeType
            // 
            shellcodeType.Controls.Add(fileShellcodeTab);
            shellcodeType.Controls.Add(RawShellcodeTab);
            shellcodeType.Controls.Add(URLShellcodeTab);
            shellcodeType.Controls.Add(genericShellcodeTab);
            shellcodeType.Location = new Point(6, 26);
            shellcodeType.Name = "shellcodeType";
            shellcodeType.SelectedIndex = 0;
            shellcodeType.Size = new Size(762, 150);
            shellcodeType.TabIndex = 14;
            // 
            // fileShellcodeTab
            // 
            fileShellcodeTab.Controls.Add(WebPayloadGenerator);
            fileShellcodeTab.Controls.Add(button1);
            fileShellcodeTab.Controls.Add(shellcodeFile);
            fileShellcodeTab.Controls.Add(label1);
            fileShellcodeTab.Location = new Point(4, 29);
            fileShellcodeTab.Name = "fileShellcodeTab";
            fileShellcodeTab.Padding = new Padding(3);
            fileShellcodeTab.Size = new Size(754, 117);
            fileShellcodeTab.TabIndex = 0;
            fileShellcodeTab.Text = "File";
            fileShellcodeTab.UseVisualStyleBackColor = true;
            // 
            // WebPayloadGenerator
            // 
            WebPayloadGenerator.Font = new Font("Segoe UI", 7.8F, FontStyle.Regular, GraphicsUnit.Point, 0);
            WebPayloadGenerator.Location = new Point(630, 47);
            WebPayloadGenerator.Margin = new Padding(2);
            WebPayloadGenerator.Name = "WebPayloadGenerator";
            WebPayloadGenerator.Size = new Size(107, 27);
            WebPayloadGenerator.TabIndex = 17;
            WebPayloadGenerator.Text = "Web Payload";
            WebPayloadGenerator.UseVisualStyleBackColor = true;
            WebPayloadGenerator.Click += WebPayloadGenerator_Click;
            // 
            // button1
            // 
            button1.Location = new Point(554, 47);
            button1.Name = "button1";
            button1.Size = new Size(71, 27);
            button1.TabIndex = 16;
            button1.Text = "Browse";
            button1.UseVisualStyleBackColor = true;
            button1.Click += button1_Click;
            // 
            // shellcodeFile
            // 
            shellcodeFile.Location = new Point(9, 47);
            shellcodeFile.Name = "shellcodeFile";
            shellcodeFile.Size = new Size(539, 27);
            shellcodeFile.TabIndex = 15;
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Location = new Point(9, 24);
            label1.Name = "label1";
            label1.Size = new Size(199, 20);
            label1.TabIndex = 14;
            label1.Text = "Shellcode file (typically .bin):";
            // 
            // RawShellcodeTab
            // 
            RawShellcodeTab.Controls.Add(RAWShellcodeInfo);
            RawShellcodeTab.Controls.Add(button2);
            RawShellcodeTab.Controls.Add(label2);
            RawShellcodeTab.Controls.Add(shellcodeRAW);
            RawShellcodeTab.Location = new Point(4, 29);
            RawShellcodeTab.Name = "RawShellcodeTab";
            RawShellcodeTab.Padding = new Padding(3);
            RawShellcodeTab.Size = new Size(754, 117);
            RawShellcodeTab.TabIndex = 1;
            RawShellcodeTab.Text = "Raw";
            RawShellcodeTab.UseVisualStyleBackColor = true;
            // 
            // RAWShellcodeInfo
            // 
            RAWShellcodeInfo.AutoSize = true;
            RAWShellcodeInfo.Font = new Font("NSimSun", 9F, FontStyle.Underline, GraphicsUnit.Point, 0);
            RAWShellcodeInfo.ForeColor = SystemColors.HotTrack;
            RAWShellcodeInfo.Location = new Point(153, 10);
            RAWShellcodeInfo.Name = "RAWShellcodeInfo";
            RAWShellcodeInfo.Size = new Size(63, 15);
            RAWShellcodeInfo.TabIndex = 12;
            RAWShellcodeInfo.Text = "Format?";
            RAWShellcodeInfo.Click += RAWShellcodeInfo_Click;
            // 
            // button2
            // 
            button2.Location = new Point(658, 83);
            button2.Name = "button2";
            button2.Size = new Size(80, 29);
            button2.TabIndex = 8;
            button2.Text = "Paste";
            button2.UseVisualStyleBackColor = true;
            button2.Click += button2_Click;
            // 
            // label2
            // 
            label2.AutoSize = true;
            label2.Location = new Point(3, 7);
            label2.Name = "label2";
            label2.Size = new Size(153, 20);
            label2.TabIndex = 6;
            label2.Text = "Paste a raw shellcode:";
            // 
            // shellcodeRAW
            // 
            shellcodeRAW.Font = new Font("Consolas", 9F, FontStyle.Regular, GraphicsUnit.Point, 0);
            shellcodeRAW.Location = new Point(3, 30);
            shellcodeRAW.Multiline = true;
            shellcodeRAW.Name = "shellcodeRAW";
            shellcodeRAW.Size = new Size(649, 82);
            shellcodeRAW.TabIndex = 7;
            // 
            // URLShellcodeTab
            // 
            URLShellcodeTab.Controls.Add(label4);
            URLShellcodeTab.Controls.Add(button3);
            URLShellcodeTab.Controls.Add(shellcodeURL);
            URLShellcodeTab.Controls.Add(label11);
            URLShellcodeTab.Location = new Point(4, 29);
            URLShellcodeTab.Name = "URLShellcodeTab";
            URLShellcodeTab.Size = new Size(754, 117);
            URLShellcodeTab.TabIndex = 2;
            URLShellcodeTab.Text = "URL";
            URLShellcodeTab.UseVisualStyleBackColor = true;
            // 
            // label4
            // 
            label4.AutoSize = true;
            label4.Font = new Font("NSimSun", 9F, FontStyle.Underline, GraphicsUnit.Point, 0);
            label4.ForeColor = SystemColors.HotTrack;
            label4.Location = new Point(98, 30);
            label4.Name = "label4";
            label4.Size = new Size(47, 15);
            label4.TabIndex = 16;
            label4.Text = "Guide";
            label4.Click += RAWShellcodeInfo_Click;
            // 
            // button3
            // 
            button3.Location = new Point(673, 50);
            button3.Name = "button3";
            button3.Size = new Size(68, 29);
            button3.TabIndex = 15;
            button3.Text = "Paste";
            button3.UseVisualStyleBackColor = true;
            button3.Click += button3_Click;
            // 
            // shellcodeURL
            // 
            shellcodeURL.Location = new Point(4, 50);
            shellcodeURL.Name = "shellcodeURL";
            shellcodeURL.Size = new Size(663, 27);
            shellcodeURL.TabIndex = 14;
            // 
            // label11
            // 
            label11.AutoSize = true;
            label11.Location = new Point(4, 27);
            label11.Name = "label11";
            label11.Size = new Size(88, 20);
            label11.TabIndex = 13;
            label11.Text = "From a URL:";
            // 
            // genericShellcodeTab
            // 
            genericShellcodeTab.Controls.Add(genericShellcodeComboBox);
            genericShellcodeTab.Controls.Add(label3);
            genericShellcodeTab.Location = new Point(4, 29);
            genericShellcodeTab.Name = "genericShellcodeTab";
            genericShellcodeTab.Size = new Size(754, 117);
            genericShellcodeTab.TabIndex = 3;
            genericShellcodeTab.Text = "Generic";
            genericShellcodeTab.UseVisualStyleBackColor = true;
            // 
            // genericShellcodeComboBox
            // 
            genericShellcodeComboBox.FormattingEnabled = true;
            genericShellcodeComboBox.Location = new Point(4, 54);
            genericShellcodeComboBox.Name = "genericShellcodeComboBox";
            genericShellcodeComboBox.Size = new Size(345, 28);
            genericShellcodeComboBox.TabIndex = 9;
            // 
            // label3
            // 
            label3.AutoSize = true;
            label3.Location = new Point(4, 31);
            label3.Name = "label3";
            label3.Size = new Size(343, 20);
            label3.TabIndex = 8;
            label3.Text = "Choose a generic shellcode (for testing purposes): ";
            // 
            // groupBox1
            // 
            groupBox1.Controls.Add(button4);
            groupBox1.Controls.Add(templateComboBox);
            groupBox1.Location = new Point(3, 298);
            groupBox1.Name = "groupBox1";
            groupBox1.Size = new Size(776, 71);
            groupBox1.TabIndex = 7;
            groupBox1.TabStop = false;
            groupBox1.Text = "Template";
            // 
            // button4
            // 
            button4.Location = new Point(640, 25);
            button4.Margin = new Padding(2);
            button4.Name = "button4";
            button4.Size = new Size(120, 27);
            button4.TabIndex = 2;
            button4.Text = "Config Template";
            button4.UseVisualStyleBackColor = true;
            button4.Click += button4_Click;
            // 
            // templateComboBox
            // 
            templateComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            templateComboBox.FormattingEnabled = true;
            templateComboBox.Location = new Point(27, 25);
            templateComboBox.Name = "templateComboBox";
            templateComboBox.Size = new Size(600, 28);
            templateComboBox.TabIndex = 1;
            templateComboBox.SelectedIndexChanged += templateComboBox_SelectedIndexChanged;
            // 
            // tabControl
            // 
            tabControl.Controls.Add(MainTab);
            tabControl.Controls.Add(PackingTab);
            tabControl.Controls.Add(BackdooringTab);
            tabControl.Location = new Point(14, 12);
            tabControl.Margin = new Padding(3, 4, 3, 4);
            tabControl.Name = "tabControl";
            tabControl.SelectedIndex = 0;
            tabControl.Size = new Size(802, 910);
            tabControl.TabIndex = 12;
            // 
            // MainForm
            // 
            AutoScaleDimensions = new SizeF(8F, 20F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = SystemColors.Control;
            ClientSize = new Size(828, 936);
            Controls.Add(tabControl);
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            MaximizeBox = false;
            Name = "MainForm";
            ShowIcon = false;
            StartPosition = FormStartPosition.CenterScreen;
            Text = "Washmachine - Loader Builder";
            Load += MainForm_Load;
            groupBox2.ResumeLayout(false);
            MainTab.ResumeLayout(false);
            MainTab.PerformLayout();
            groupBox3.ResumeLayout(false);
            groupBox3.PerformLayout();
            shellcode.ResumeLayout(false);
            shellcodeType.ResumeLayout(false);
            fileShellcodeTab.ResumeLayout(false);
            fileShellcodeTab.PerformLayout();
            RawShellcodeTab.ResumeLayout(false);
            RawShellcodeTab.PerformLayout();
            URLShellcodeTab.ResumeLayout(false);
            URLShellcodeTab.PerformLayout();
            genericShellcodeTab.ResumeLayout(false);
            genericShellcodeTab.PerformLayout();
            groupBox1.ResumeLayout(false);
            tabControl.ResumeLayout(false);
            ResumeLayout(false);
        }

        #endregion
        private TextBox textBox4;
        private RichTextBox debugBox;
        private GroupBox groupBox2;
        private ComboBox comboBox5;
        private ComboBox comboBox4;
        private TabPage BackdooringTab;
        private TabPage PackingTab;
        private TabPage MainTab;
        private GroupBox groupBox3;
        private ComboBox bin2hexEncoder;
        private ComboBox bin2hexEnvelope;
        private Label payloadEncodingEnvelopeLabel;
        private Label payloadEncodingEncoderLabel;
        private GroupBox shellcode;
        private GroupBox groupBox1;
        private ComboBox templateComboBox;
        private Button submitButton;
        private TabControl tabControl;
        private Button button4;
        private TabControl shellcodeType;
        private TabPage fileShellcodeTab;
        private Button WebPayloadGenerator;
        private Button button1;
        private TextBox shellcodeFile;
        private Label label1;
        private TabPage RawShellcodeTab;
        private Label RAWShellcodeInfo;
        private Button button2;
        private Label label2;
        private TextBox shellcodeRAW;
        private TabPage URLShellcodeTab;
        private Label label4;
        private Button button3;
        private TextBox shellcodeURL;
        private Label label11;
        private TabPage genericShellcodeTab;
        private ComboBox genericShellcodeComboBox;
        private Label label3;
    }
}
