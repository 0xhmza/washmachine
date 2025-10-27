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
            groupBox1 = new GroupBox();
            templateLabel = new Label();
            templateComboBox = new ComboBox();
            SnippetsPicker = new FlowLayoutPanel();
            shellcode = new GroupBox();
            label1 = new Label();
            shellcodeFile = new TextBox();
            button1 = new Button();
            label2 = new Label();
            shellcodeRAW = new TextBox();
            button2 = new Button();
            label3 = new Label();
            genericShellcodeComboBox = new ComboBox();
            label11 = new Label();
            shellcodeURL = new TextBox();
            button3 = new Button();
            RAWShellcodeInfo = new Label();
            tabControl = new TabControl();
            groupBox3 = new GroupBox();
            payloadEncodingEnvelopeLabel = new Label();
            payloadEncodingEncoderLabel = new Label();
            bin2hexEncoder = new ComboBox();
            bin2hexEnvelope = new ComboBox();
            groupBox2.SuspendLayout();
            MainTab.SuspendLayout();
            groupBox1.SuspendLayout();
            shellcode.SuspendLayout();
            tabControl.SuspendLayout();
            groupBox3.SuspendLayout();
            SuspendLayout();
            // 
            // textBox4
            // 
            textBox4.BackColor = SystemColors.Menu;
            textBox4.BorderStyle = BorderStyle.None;
            textBox4.Enabled = false;
            textBox4.ForeColor = Color.Brown;
            textBox4.Location = new Point(1069, 1015);
            textBox4.Margin = new Padding(4);
            textBox4.Multiline = true;
            textBox4.Name = "textBox4";
            textBox4.Size = new Size(840, 104);
            textBox4.TabIndex = 8;
            textBox4.Text = resources.GetString("textBox4.Text");
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
            // BackdooringTab
            // 
            BackdooringTab.Location = new Point(4, 34);
            BackdooringTab.Margin = new Padding(4, 5, 4, 5);
            BackdooringTab.Name = "BackdooringTab";
            BackdooringTab.Size = new Size(1021, 1066);
            BackdooringTab.TabIndex = 3;
            BackdooringTab.Text = "Backdooring";
            BackdooringTab.UseVisualStyleBackColor = true;
            // 
            // PackingTab
            // 
            PackingTab.Location = new Point(4, 34);
            PackingTab.Margin = new Padding(4, 5, 4, 5);
            PackingTab.Name = "PackingTab";
            PackingTab.Size = new Size(1021, 1066);
            PackingTab.TabIndex = 2;
            PackingTab.Text = "Packing";
            PackingTab.UseVisualStyleBackColor = true;
            // 
            // MainTab
            // 
            MainTab.Controls.Add(groupBox3);
            MainTab.Controls.Add(shellcode);
            MainTab.Controls.Add(groupBox1);
            MainTab.Controls.Add(submitButton);
            MainTab.Location = new Point(4, 34);
            MainTab.Margin = new Padding(4, 5, 4, 5);
            MainTab.Name = "MainTab";
            MainTab.Padding = new Padding(4, 5, 4, 5);
            MainTab.Size = new Size(1021, 1066);
            MainTab.TabIndex = 0;
            MainTab.Text = "Main";
            MainTab.UseVisualStyleBackColor = true;
            // 
            // submitButton
            // 
            submitButton.Location = new Point(437, 1021);
            submitButton.Margin = new Padding(4);
            submitButton.Name = "submitButton";
            submitButton.Size = new Size(154, 36);
            submitButton.TabIndex = 9;
            submitButton.Text = "Compile";
            submitButton.UseVisualStyleBackColor = true;
            submitButton.Click += submitButton_Click;
            // 
            // groupBox1
            // 
            groupBox1.Controls.Add(SnippetsPicker);
            groupBox1.Controls.Add(templateComboBox);
            groupBox1.Controls.Add(templateLabel);
            groupBox1.Location = new Point(4, 619);
            groupBox1.Margin = new Padding(4);
            groupBox1.Name = "groupBox1";
            groupBox1.Padding = new Padding(4);
            groupBox1.Size = new Size(996, 394);
            groupBox1.TabIndex = 7;
            groupBox1.TabStop = false;
            groupBox1.Text = "Parameters";
            // 
            // templateLabel
            // 
            templateLabel.AutoSize = true;
            templateLabel.Location = new Point(12, 35);
            templateLabel.Margin = new Padding(4, 0, 4, 0);
            templateLabel.Name = "templateLabel";
            templateLabel.Size = new Size(133, 25);
            templateLabel.TabIndex = 0;
            templateLabel.Text = "Code template:";
            // 
            // templateComboBox
            // 
            templateComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            templateComboBox.FormattingEnabled = true;
            templateComboBox.Location = new Point(152, 31);
            templateComboBox.Margin = new Padding(4);
            templateComboBox.Name = "templateComboBox";
            templateComboBox.Size = new Size(430, 33);
            templateComboBox.TabIndex = 1;
            templateComboBox.SelectedIndexChanged += templateComboBox_SelectedIndexChanged;
            // 
            // SnippetsPicker
            // 
            SnippetsPicker.AutoScroll = true;
            SnippetsPicker.Location = new Point(7, 80);
            SnippetsPicker.Name = "SnippetsPicker";
            SnippetsPicker.Size = new Size(982, 306);
            SnippetsPicker.TabIndex = 2;
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
            shellcode.Location = new Point(4, 19);
            shellcode.Margin = new Padding(4);
            shellcode.Name = "shellcode";
            shellcode.Padding = new Padding(4);
            shellcode.Size = new Size(996, 479);
            shellcode.TabIndex = 6;
            shellcode.TabStop = false;
            shellcode.Text = "Shellcode";
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
            shellcodeFile.Margin = new Padding(4);
            shellcodeFile.Name = "shellcodeFile";
            shellcodeFile.Size = new Size(828, 31);
            shellcodeFile.TabIndex = 1;
            // 
            // button1
            // 
            button1.Location = new Point(870, 76);
            button1.Margin = new Padding(4);
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
            shellcodeRAW.Margin = new Padding(4);
            shellcodeRAW.Multiline = true;
            shellcodeRAW.Name = "shellcodeRAW";
            shellcodeRAW.Size = new Size(828, 125);
            shellcodeRAW.TabIndex = 4;
            // 
            // button2
            // 
            button2.Location = new Point(870, 245);
            button2.Margin = new Padding(4);
            button2.Name = "button2";
            button2.Size = new Size(100, 36);
            button2.TabIndex = 5;
            button2.Text = "Paste";
            button2.UseVisualStyleBackColor = true;
            button2.Click += button2_Click;
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
            // genericShellcodeComboBox
            // 
            genericShellcodeComboBox.FormattingEnabled = true;
            genericShellcodeComboBox.Location = new Point(34, 420);
            genericShellcodeComboBox.Margin = new Padding(4);
            genericShellcodeComboBox.Name = "genericShellcodeComboBox";
            genericShellcodeComboBox.Size = new Size(430, 33);
            genericShellcodeComboBox.TabIndex = 7;
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
            // shellcodeURL
            // 
            shellcodeURL.Location = new Point(34, 334);
            shellcodeURL.Margin = new Padding(4);
            shellcodeURL.Name = "shellcodeURL";
            shellcodeURL.Size = new Size(828, 31);
            shellcodeURL.TabIndex = 9;
            // 
            // button3
            // 
            button3.Location = new Point(870, 334);
            button3.Margin = new Padding(4);
            button3.Name = "button3";
            button3.Size = new Size(100, 36);
            button3.TabIndex = 10;
            button3.Text = "Paste";
            button3.UseVisualStyleBackColor = true;
            button3.Click += button3_Click;
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
            // tabControl
            // 
            tabControl.Controls.Add(MainTab);
            tabControl.Controls.Add(PackingTab);
            tabControl.Controls.Add(BackdooringTab);
            tabControl.Location = new Point(18, 15);
            tabControl.Margin = new Padding(4, 5, 4, 5);
            tabControl.Name = "tabControl";
            tabControl.SelectedIndex = 0;
            tabControl.Size = new Size(1029, 1104);
            tabControl.TabIndex = 12;
            // 
            // groupBox3
            // 
            groupBox3.Controls.Add(bin2hexEncoder);
            groupBox3.Controls.Add(bin2hexEnvelope);
            groupBox3.Controls.Add(payloadEncodingEnvelopeLabel);
            groupBox3.Controls.Add(payloadEncodingEncoderLabel);
            groupBox3.Location = new Point(4, 505);
            groupBox3.Name = "groupBox3";
            groupBox3.Size = new Size(996, 107);
            groupBox3.TabIndex = 10;
            groupBox3.TabStop = false;
            groupBox3.Text = "Payload Encoding";
            // 
            // payloadEncodingEnvelopeLabel
            // 
            payloadEncodingEnvelopeLabel.AutoSize = true;
            payloadEncodingEnvelopeLabel.Location = new Point(539, 48);
            payloadEncodingEnvelopeLabel.Margin = new Padding(4, 0, 4, 0);
            payloadEncodingEnvelopeLabel.Name = "payloadEncodingEnvelopeLabel";
            payloadEncodingEnvelopeLabel.Size = new Size(88, 25);
            payloadEncodingEnvelopeLabel.TabIndex = 4;
            payloadEncodingEnvelopeLabel.Text = "Envelope:";
            // 
            // payloadEncodingEncoderLabel
            // 
            payloadEncodingEncoderLabel.AutoSize = true;
            payloadEncodingEncoderLabel.Location = new Point(87, 48);
            payloadEncodingEncoderLabel.Margin = new Padding(4, 0, 4, 0);
            payloadEncodingEncoderLabel.Name = "payloadEncodingEncoderLabel";
            payloadEncodingEncoderLabel.Size = new Size(80, 25);
            payloadEncodingEncoderLabel.TabIndex = 3;
            payloadEncodingEncoderLabel.Text = "Encoder:";
            // 
            // bin2hexEncoder
            // 
            bin2hexEncoder.FormattingEnabled = true;
            bin2hexEncoder.Location = new Point(171, 45);
            bin2hexEncoder.Margin = new Padding(4, 5, 4, 5);
            bin2hexEncoder.Name = "bin2hexEncoder";
            bin2hexEncoder.Size = new Size(260, 33);
            bin2hexEncoder.TabIndex = 8;
            // 
            // bin2hexEnvelope
            // 
            bin2hexEnvelope.FormattingEnabled = true;
            bin2hexEnvelope.Location = new Point(631, 45);
            bin2hexEnvelope.Margin = new Padding(4, 5, 4, 5);
            bin2hexEnvelope.Name = "bin2hexEnvelope";
            bin2hexEnvelope.Size = new Size(260, 33);
            bin2hexEnvelope.TabIndex = 7;
            // 
            // MainForm
            // 
            AutoScaleDimensions = new SizeF(10F, 25F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = SystemColors.Control;
            ClientSize = new Size(1926, 1136);
            Controls.Add(tabControl);
            Controls.Add(groupBox2);
            Controls.Add(textBox4);
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            Margin = new Padding(4);
            MaximizeBox = false;
            Name = "MainForm";
            ShowIcon = false;
            StartPosition = FormStartPosition.CenterScreen;
            Text = "Washmachine - Loader Builder";
            Load += MainForm_Load;
            groupBox2.ResumeLayout(false);
            MainTab.ResumeLayout(false);
            groupBox1.ResumeLayout(false);
            groupBox1.PerformLayout();
            shellcode.ResumeLayout(false);
            shellcode.PerformLayout();
            tabControl.ResumeLayout(false);
            groupBox3.ResumeLayout(false);
            groupBox3.PerformLayout();
            ResumeLayout(false);
            PerformLayout();
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
        private Label RAWShellcodeInfo;
        private Button button3;
        private TextBox shellcodeURL;
        private Label label11;
        private ComboBox genericShellcodeComboBox;
        private Label label3;
        private Button button2;
        private TextBox shellcodeRAW;
        private Label label2;
        private Button button1;
        private TextBox shellcodeFile;
        private Label label1;
        private GroupBox groupBox1;
        private FlowLayoutPanel SnippetsPicker;
        private ComboBox templateComboBox;
        private Label templateLabel;
        private Button submitButton;
        private TabControl tabControl;
    }
}
