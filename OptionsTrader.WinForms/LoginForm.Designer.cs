namespace OptionsTrader.WinForms;

partial class LoginForm
{
    private System.ComponentModel.IContainer components = null;

    protected override void Dispose(bool disposing)
    {
        if (disposing && (components != null))
            components.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        lblUsername = new Label();
        txtUsername = new TextBox();
        lblPassword = new Label();
        txtPassword = new TextBox();
        btnEnter = new Button();
        lblError = new Label();
        SuspendLayout();
        //
        // lblUsername
        //
        lblUsername.Location = new Point(12, 20);
        lblUsername.Name = "lblUsername";
        lblUsername.Size = new Size(75, 20);
        lblUsername.TabIndex = 0;
        lblUsername.Text = "Username:";
        //
        // txtUsername
        //
        txtUsername.Location = new Point(95, 18);
        txtUsername.Name = "txtUsername";
        txtUsername.Size = new Size(175, 23);
        txtUsername.TabIndex = 1;
        //
        // lblPassword
        //
        lblPassword.Location = new Point(12, 52);
        lblPassword.Name = "lblPassword";
        lblPassword.Size = new Size(75, 20);
        lblPassword.TabIndex = 2;
        lblPassword.Text = "Password:";
        //
        // txtPassword
        //
        txtPassword.Location = new Point(95, 50);
        txtPassword.Name = "txtPassword";
        txtPassword.Size = new Size(175, 23);
        txtPassword.TabIndex = 3;
        txtPassword.UseSystemPasswordChar = true;
        //
        // btnEnter
        //
        btnEnter.Location = new Point(190, 82);
        btnEnter.Name = "btnEnter";
        btnEnter.Size = new Size(80, 25);
        btnEnter.TabIndex = 4;
        btnEnter.Text = "Enter";
        btnEnter.Click += BtnEnter_Click;
        //
        // lblError
        //
        lblError.ForeColor = Color.Red;
        lblError.Location = new Point(12, 82);
        lblError.Name = "lblError";
        lblError.Size = new Size(172, 40);
        lblError.TabIndex = 5;
        lblError.Visible = false;
        //
        // LoginForm
        //
        AcceptButton = btnEnter;
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(284, 121);
        Controls.Add(lblUsername);
        Controls.Add(txtUsername);
        Controls.Add(lblPassword);
        Controls.Add(txtPassword);
        Controls.Add(btnEnter);
        Controls.Add(lblError);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Name = "LoginForm";
        StartPosition = FormStartPosition.CenterScreen;
        Text = "Options Trader — Login";
        ResumeLayout(false);
        PerformLayout();
    }

    private Label lblUsername;
    private TextBox txtUsername;
    private Label lblPassword;
    private TextBox txtPassword;
    private Button btnEnter;
    private Label lblError;
}
