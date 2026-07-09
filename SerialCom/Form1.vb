Imports System
Imports System.Threading
Imports System.Threading.Tasks
Imports System.IO.Ports
Imports System.IO
Imports System.ComponentModel
Imports System.Net
Imports System.Net.NetworkInformation
Imports System.Runtime.InteropServices
Imports System.Reflection



Public Class Form1
    '------------------------------------------------
    Dim myPort As Array
    Delegate Sub SetTextCallback(ByVal [text] As String) 'Added to prevent threading errors during receiveing of data
    Dim result As String
    Dim debugFileContent As String
    Dim isConnecting As Boolean = False
    Dim isConfiguring As Boolean = False
    Dim isReadingParameter As Boolean = False
    Dim isSyncTime As Boolean = False
    ' Guards the automatic teardown+reconnect kicked off from ProcessLoggerResponse when the
    ' running device drops the session ("Password Incorrect"). Prevents overlapping cycles
    ' when several such bytes arrive across rapid DataReceived events. Reset in BtnConnect_Click.
    Dim isAutoReconnecting As Boolean = False
    Dim DeviceParameterString As String

    '--- Thread synchronization (bugs #3, #4, #8) ---
    ' stateLock guards the cross-thread flags (_isWaiting/_sending) and the receive buffer.
    Private ReadOnly stateLock As New Object()
    ' serialLock serializes every SerialPort1.Write so a UI-thread click can't interleave
    ' bytes with the background SendLoop.
    Private ReadOnly serialLock As New Object()
    Private _isWaiting As Boolean = False
    Private _sending As Boolean = False
    ' Accumulated received bytes, so a response split across multiple DataReceived events
    ' (e.g. "O" then "K") is still detected.
    Private receiveBuffer As String = ""
    ' When the current queued command started waiting for its ack, for timeout fallback.
    Private waitStartTime As DateTime = DateTime.MinValue
    Private Const WaitTimeoutMs As Integer = 15000

    ' Thread-safe accessors for the send-queue handshake flags.
    Private Property IsWaitingFlag As Boolean
        Get
            SyncLock stateLock
                Return _isWaiting
            End SyncLock
        End Get
        Set(value As Boolean)
            SyncLock stateLock
                _isWaiting = value
            End SyncLock
        End Set
    End Property

    Private Property SendingFlag As Boolean
        Get
            SyncLock stateLock
                Return _sending
            End SyncLock
        End Get
        Set(value As Boolean)
            SyncLock stateLock
                _sending = value
            End SyncLock
        End Set
    End Property

    ' Append to the receive buffer and return a snapshot for Contains() checks.
    Private Function AppendToReceiveBuffer(chunk As String) As String
        SyncLock stateLock
            receiveBuffer &= chunk
            Return receiveBuffer
        End SyncLock
    End Function

    Private Sub ClearReceiveBuffer()
        SyncLock stateLock
            receiveBuffer = ""
        End SyncLock
    End Sub

    Dim isCheckingFlowmeter As Boolean = False
    Dim flowmeterType As String
    Dim aichiList As New List(Of String) From {"1", "2", "3", "868977089837892"}

    ' Lớp chứa dữ liệu và delay
    Private Class SendItem
        Public Property Data As String
        Public Property DelayMs As Integer
    End Class
    Dim sendQueue As New Queue(Of SendItem)
    Dim sendingThread As Thread


    '------------------------------------------------
    Private Sub Form1_Load(sender As System.Object, e As System.EventArgs) Handles MyBase.Load

        Dim pos = Label4.Parent.PointToScreen(Label4.Location)
        pos = PictureBox2.PointToClient(pos)
        Label4.Parent = PictureBox2
        Label4.Location = pos

        myPort = IO.Ports.SerialPort.GetPortNames()
        ComboBox1.Items.AddRange(myPort)
        ComboBox2.Items.AddRange(myPort)

        Button1.Enabled = True
        Button9.Enabled = False
        Button12.Enabled = False
        Button14.Enabled = False
        Button15.Enabled = False
        Button4.Enabled = False

        Button8.Enabled = True
        Button7.Enabled = False

        ' logTime (ComboBox4) must not be selectable until sendTime (ComboBox3) is chosen,
        ' since RebuildLogTimeItems filters the logTime options against the selected sendTime.
        ComboBox4.Enabled = False

        SerialPort1.Close()
        SerialPort2.Close()

        Dim Licence = New String() {"482AE34A57D6", "7604F116D023", "703217C7D56D", "38FC98E41FCF", "38FC98E2CF35", "5EFB3A99D2E7"}
        Dim nics() As NetworkInterface = NetworkInterface.GetAllNetworkInterfaces()
        ' Bug #6: machines with 0-1 NICs would throw on nics(1). Pick index 1 if present,
        ' otherwise fall back to the first NIC, otherwise no MAC available.
        Dim nic As NetworkInterface = Nothing
        If nics.Length > 1 Then
            nic = nics(1)
        ElseIf nics.Length = 1 Then
            nic = nics(0)
        End If
        If nic IsNot Nothing Then
            Dim mac As String = nic.GetPhysicalAddress().ToString()
            Code.Text = mac
            If (Array.FindIndex(Licence, Function(x) x = mac) = -1) Then
                'Me.Close()
                'Button1.Enabled = False
                'Button9.Enabled = False
            End If
        End If
    End Sub

    Private Sub SerialPortLogger_DataReceived(sender As System.Object, e As System.IO.Ports.SerialDataReceivedEventArgs) Handles SerialPort1.DataReceived
        ProcessLoggerResponse(SerialPort1.ReadExisting())
    End Sub

    Private Sub SerialPortDebug_DataReceived(sender As Object, e As SerialDataReceivedEventArgs) Handles SerialPort2.DataReceived
        ProcessDebugResponse(SerialPort2.ReadExisting())
    End Sub

    Private Sub ProcessLoggerResponse(ByVal [text] As String) 'input from ReadExisting
        If Me.RichTextBox2.InvokeRequired Then
            Dim x As New SetTextCallback(AddressOf ProcessLoggerResponse)
            Me.Invoke(x, New Object() {(text)})
        Else
            debugFileContent &= [text]
            DeviceParameterString &= [text]
            ' Bug #8: accumulate chunks so a reply split across DataReceived events is still
            ' matched. buffer is the running accumulation used for the checks below.
            Dim buffer As String = AppendToReceiveBuffer([text])
            'If [text].Contains("OK") And Not ([text].Contains(TextBox2.Text)) And Not isConnecting And isConfiguring Then
            '    Me.RichTextBox2.Text = vbCr & "Configure done" & vbCr
            'End If

            ' Running device dropped the session and wants a fresh AT/password handshake.
            ' Silently tear down and reconnect (no blocking dialog). isAutoReconnecting stops
            ' repeated "Password Incorrect" bytes from starting overlapping reconnect cycles.
            If (buffer.Contains("Password Incorrect") And Button9.Enabled) AndAlso Not isAutoReconnecting Then
                isAutoReconnecting = True
                ' Stop and drain the live SendLoop/queue first: if the drop hit mid-Configure,
                ' the still-running SendLoop would otherwise write leftover AT+CFGDEV/COMMANDn
                ' commands interleaved with the new handshake's "AT"/password on the reopened port.
                SendingFlag = False
                ' Join before Close(): serialLock doesn't guard Close(), so without this a
                ' thread paused mid-Write could still land one stale command on the reopened port.
                Try
                    sendingThread?.Join(3000)
                Catch
                End Try
                SyncLock sendQueue
                    sendQueue.Clear()
                End SyncLock
                IsWaitingFlag = False
                ' Clear stale mode flags so the new handshake's "OK" isn't misread as a
                ' Configure ack or a Load-Parameters read-back line.
                isConfiguring = False
                isReadingParameter = False
                BtnDisconnect_Click(Nothing, Nothing)
                BtnConnect_Click(Nothing, Nothing)
                ' Safety: if ComboBox1 was empty, BtnConnect_Click returns before its Task.Run
                ' Finally runs, so clear the guard here too and never leave it stuck True.
                isAutoReconnecting = False
            End If

            ' Bug #8: check the accumulated buffer, not just this chunk, so "O"+"K" split
            ' across events still registers as a successful connect.
            If ((buffer.Contains("Password Correct") Or buffer.Contains("OK"))) And isConnecting Then
                isConnecting = False
                ' Append status so it adds a line into the ongoing raw log instead of erasing it.
                Me.RichTextBox2.AppendText(vbCr & "Connected" & vbCr)
                Button1.Enabled = False
                Button9.Enabled = True
                Button12.Enabled = True
                Button14.Enabled = True
                Button15.Enabled = True
                Button4.Enabled = True
                ClearReceiveBuffer()
            End If

            [text] = RebrandLoggerText([text])
            RichTextBox4.Text &= [text]
            RichTextBox4.SelectionStart = RichTextBox4.TextLength
            RichTextBox4.ScrollToCaret()

            ' User request: show EVERY raw device response chunk in the orange log box
            ' (RichTextBox2), not only while isConfiguring. [text] already had the
            ' AT+ -> VILOG_ and DRAGINO -> BAVITECH substitutions applied above (for
            ' RichTextBox4), so reuse it here.
            RichTextBox2.Text &= [text]
            RichTextBox2.SelectionStart = RichTextBox2.TextLength
            RichTextBox2.ScrollToCaret()

            If isConfiguring Then
                ' Bug #8: power-off ack may also arrive split across events.
                If (buffer.Contains("NB module power-off successful.")) Then
                    isConfiguring = False
                End If

                ' Bugs #1/#2/#8: unblock the send queue on the device ack.
                ' - Match "OK" specifically (not a bare "K", which matched mid-word e.g. "CLOCKLOG").
                ' - Also unblock on "ERROR" so a rejected command doesn't hang the queue forever.
                ' - Check the accumulated buffer so "O"+"K" split across events is caught.
                If buffer.Contains("OK") Or buffer.Contains("ERROR") Then
                    IsWaitingFlag = False
                    ClearReceiveBuffer()
                End If
            End If



            'If isCheckingFlowmeter Then
            '    RichTextBox2.Text = [text]
            '    If aichiList.Contains([text]) Then
            '        MessageBox.Show("aichi")
            '        flowmeterType = "Aichi Tokei"
            '    End If
            '    isCheckingFlowmeter = False
            'End If

            If isReadingParameter Then
                ' Bug #13: devices may terminate lines with bare vbLf, not vbCrLf. Normalize
                ' both CrLf and lone Cr to Lf before splitting so parsing doesn't silently break.
                Dim normalized As String = DeviceParameterString.Replace(vbCrLf, vbLf).Replace(vbCr, vbLf)
                Dim Parameter() As String = normalized.Split(CChar(vbLf))
                For i As Integer = 0 To Parameter.Length - 1
                    If Parameter(i).Contains("AT+PUBTOPIC=") Then
                        ' Bug #5: malformed topic (too few "_" fields) must not crash the app.
                        Try
                            Dim Topic As String = Parameter(i).Substring(12)
                            Dim SiteId() As String = Split(Topic, "_")
                            TextBox12.Text = SiteId(1)
                            TextBox11.Text = SiteId(2)
                        Catch ex As Exception
                            ' ignore malformed PUBTOPIC line
                        End Try
                    End If
                    If Parameter(i).Contains("AT+TDC=") Then
                        ' Bug #5: malformed/truncated TDC line (empty or non-numeric after the
                        ' "AT+TDC=" cut) must not crash the app.
                        Try
                            Dim TDC As String = Parameter(i).Substring(7)
                            Dim TDC_Integer As Integer = CInt(TDC)
                            If TDC_Integer < 3000 Then
                                ComboBox3.SelectedItem = CStr(TDC_Integer / 60) & "m"
                            Else
                                ComboBox3.SelectedItem = CStr(TDC_Integer / 3600) & "h"
                            End If
                        Catch ex As Exception
                            ' ignore malformed TDC line
                        End Try
                    End If
                    If Parameter(i).Contains("AT+COMMAND1=01 03 01 10 00 10") Then
                        ComboBox5.SelectedItem = "Aichi Tokei"
                    ElseIf Parameter(i).Contains("AT+COMMAND1=01 04 0f a2 00 02") Then
                        ComboBox5.SelectedItem = "Krohne"
                    End If
                    If Parameter(i).Contains("AT+CLOCKLOG=") Then
                        ' Bug #5: malformed CLOCKLOG line (too few fields / non-numeric) must not
                        ' crash the app.
                        Try
                            Dim log As String = Parameter(i).Substring(12)
                            Dim x() As String = Split(log, ",")
                            Dim log_Integer As Integer = CInt(x(2))
                            Dim log_Aichi_Interger As Integer = CInt(x(1))
                            ' Faithful read-back display must win over the proactive logTime
                            ' filter. ComboBox3/ComboBox5 were set above and already fired
                            ' RebuildLogTimeItems, which may have trimmed the device's real
                            ' logTime label out of ComboBox4.Items. Re-add it if missing so the
                            ' assignment isn't silently dropped (SelectedItem no-ops on a label
                            ' not present in Items). This only touches Items transiently: any
                            ' later manual sendTime/sensor change re-runs RebuildLogTimeItems,
                            ' which rebuilds Items from the sensor-aware candidate set.
                            Dim logLabel As String
                            If log_Aichi_Interger = 0 Then
                                logLabel = CStr(log_Integer) & "m"
                            Else
                                logLabel = CStr(log_Aichi_Interger) & "m"
                            End If
                            If Not ComboBox4.Items.Contains(logLabel) Then
                                ComboBox4.Items.Add(logLabel)
                            End If
                            ComboBox4.SelectedItem = logLabel
                        Catch ex As Exception
                            ' ignore malformed CLOCKLOG line
                        End Try
                    End If
                    If Parameter(i).Contains("OK") Then
                        ' Append status so it adds a line into the ongoing raw log instead of erasing it.
                        Me.RichTextBox2.AppendText(vbCr & "Load parameters completed" & vbCr)
                        isReadingParameter = False
                    End If

                Next
            End If
        End If
    End Sub

    Private Sub ProcessDebugResponse(ByVal [text] As String) 'input from ReadExisting
        If Me.RichTextBox3.InvokeRequired Then
            Dim x As New SetTextCallback(AddressOf ProcessDebugResponse)
            Me.Invoke(x, New Object() {(text)})
        Else
            [text] = RebrandDebugText([text])
            Me.RichTextBox3.Text &= [text]
            RichTextBox3.SelectionStart = RichTextBox3.TextLength
            RichTextBox3.ScrollToCaret()
        End If
    End Sub

    Private Sub ProcessMonitorResponse(ByVal [text] As String) 'input from ReadExisting
        [text] = RebrandDeviceNameText([text])
        Me.result &= [text]
    End Sub

    ''' <summary>
    ''' Cosmetic rebrand shared by every serial link: hides the physical device
    ''' brand name ("DRAGINO") behind "BAVITECH" before text reaches the user.
    ''' No reverse mapping exists for this one (unlike AT+/VILOG_), so it's safe
    ''' to apply unconditionally on any inbound text.
    ''' </summary>
    Private Function RebrandDeviceNameText(ByVal [text] As String) As String
        If ([text].Contains("DRAGINO")) Then
            Return [text].Replace("DRAGINO", "BAVITECH")
        End If
        Return [text]
    End Function

    ''' <summary>
    ''' Cosmetic rebrand for SerialPort1 (logger/config link, feeds RichTextBox2/4):
    ''' "AT+" -> "VILOG_" (uppercase, this link's convention) plus the shared
    ''' DRAGINO -> BAVITECH device-name rebrand.
    ''' </summary>
    Private Function RebrandLoggerText(ByVal [text] As String) As String
        If ([text].Contains("AT+")) Then
            [text] = [text].Replace("AT+", "VILOG_")
        End If
        Return RebrandDeviceNameText([text])
    End Function

    ''' <summary>
    ''' Cosmetic rebrand for SerialPort2 (secondary/local debug link, feeds
    ''' RichTextBox3): "AT+" -> "Vilog_" (mixed-case, this link's convention).
    ''' Reversed on send via RichTextBox1.Text.Replace("Vilog_", "AT+") -
    ''' keep this spelling in sync with that reverse mapping.
    ''' </summary>
    Private Function RebrandDebugText(ByVal [text] As String) As String
        If ([text].Contains("AT+")) Then
            Return [text].Replace("AT+", "Vilog_")
        End If
        Return [text]
    End Function

    Private Sub BtnRefreshConfigPorts_Click(sender As Object, e As EventArgs) Handles Button5.Click
        myPort = IO.Ports.SerialPort.GetPortNames()
        ComboBox1.Items.Clear()
        ComboBox1.Items.AddRange(myPort)
    End Sub

    Private Sub BtnConnect_Click(sender As Object, e As EventArgs) Handles Button1.Click
        Me.RichTextBox2.Text = ""
        If (ComboBox1.Text <> Nothing) Then
            SerialPort2.Close()
            Button8.Enabled = True
            Button7.Enabled = False

            SerialPort1.Close()

            SerialPort1.PortName = ComboBox1.Text
            SerialPort1.BaudRate = 9600
            ' Review Finding 1: bound Write/Read so a stalled write can't block the thread
            ' holding serialLock forever (which would freeze any UI click that also needs it).
            SerialPort1.WriteTimeout = 2000
            SerialPort1.ReadTimeout = 2000

            Try
                SerialPort1.Open()
            Catch ex As Exception
                SerialPort1.Close()
                ' Bug #7: reset isConnecting on the failure path so a later stray "OK"/
                ' "Password Correct" byte is not misread as a successful connect.
                isConnecting = False
                isAutoReconnecting = False
                RichTextBox2.AppendText("Logger port open error: " & ex.Message & vbCrLf)
                GoTo cannot_connect
            End Try

            ' Only arm the connect detection once the port actually opened.
            isConnecting = True
            ClearReceiveBuffer()

            ' Bug #15: run the AT/password handshake waits off the UI thread so the UI
            ' doesn't freeze. Capture the password on the UI thread first.
            Dim pwd As String = TextBox2.Text
            Task.Run(Sub()
                         Try
                             Thread.Sleep(1000)
                             SyncLock serialLock
                                 SerialPort1.Write("AT" & vbCr)
                             End SyncLock
                             Thread.Sleep(1000)
                             SyncLock serialLock
                                 SerialPort1.Write(pwd & vbCr)
                             End SyncLock
                         Catch ex As Exception
                             ' port may have closed; connect detection times out on its own
                             Me.Invoke(New Action(Sub()
                                                      RichTextBox2.AppendText("Handshake error: " & ex.Message & vbCrLf)
                                                  End Sub))
                         Finally
                             ' Reconnect attempt finished (success or failure): allow a future
                             ' auto-reconnect. Harmless no-op for a normal user-initiated connect.
                             isAutoReconnecting = False
                         End Try
                     End Sub)
cannot_connect:
        End If
    End Sub

    Private Sub BtnDisconnect_Click(sender As Object, e As EventArgs) Handles Button4.Click
        SerialPort1.Close()
        Me.RichTextBox2.Text = ""
        Button1.Enabled = True
        Button9.Enabled = False
        Button12.Enabled = False
        Button14.Enabled = False
        Button15.Enabled = False
        Button4.Enabled = False
    End Sub

    Private Sub BtnConfigure_Click(sender As Object, e As EventArgs) Handles Button9.Click

        ' Kiem tra thieu thong tin truoc khi cau hinh
        Dim missing As New List(Of String)
        If String.IsNullOrWhiteSpace(TextBox11.Text) Then missing.Add("ID")
        If String.IsNullOrWhiteSpace(TextBox12.Text) Then missing.Add("Site Name")
        If String.IsNullOrEmpty(ComboBox5.Text) Then missing.Add("Meter")
        If String.IsNullOrEmpty(ComboBox3.Text) Then missing.Add("Cycle Send")
        If String.IsNullOrEmpty(ComboBox4.Text) Then missing.Add("Cycle Log")
        If missing.Count > 0 Then
            MessageBox.Show("Thiếu thông tin: " & String.Join(", ", missing), "Thiếu thông tin", MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Exit Sub
        End If

        'isCheckingFlowmeter = True
        'Me.RichTextBox2.Text = "Checking Device, please wait...!" & vbCr
        'SerialPort1.Write("AT+DEUI=?" & vbCr)
        'Thread.Sleep(1000)
        'ComboBox5.Text = flowmeterType
        'Thread.Sleep(1000)

        Me.RichTextBox2.Text = "Configuring, please wait...!" & vbCr
        Thread.Sleep(500)

        sendQueue.Clear()
        Config()
        AddToQueue("ATZ" & vbCr, 200)

        ' Fresh handshake state for this config run.
        IsWaitingFlag = False
        ClearReceiveBuffer()
        SendingFlag = True
        ' Tạo thread gửi dữ liệu
        sendingThread = New Thread(AddressOf SendLoop)
        sendingThread.IsBackground = True
        sendingThread.Start()

    End Sub

    Private Sub SendLoop()
        While SendingFlag
            Dim item As SendItem = Nothing

            ' Bug #1: timeout fallback. If the device never acks (ERROR/OK never arrives,
            ' or the reply is lost), unblock after WaitTimeoutMs so the queue doesn't hang
            ' forever. Normal unblocking happens in ProcessLoggerResponse on the "OK"/"ERROR" ack.
            If IsWaitingFlag Then
                If (DateTime.Now - waitStartTime).TotalMilliseconds > WaitTimeoutMs Then
                    IsWaitingFlag = False
                End If
            End If

            If Not IsWaitingFlag Then
                SyncLock sendQueue
                    If sendQueue.Count > 0 Then
                        item = sendQueue.Dequeue()
                    Else
                        SendingFlag = False
                        Exit While
                    End If
                End SyncLock

                If item IsNot Nothing Then
                    Try
                        IsWaitingFlag = True
                        waitStartTime = DateTime.Now
                        ' Bug #8: start each command's ack detection from a clean buffer so a
                        ' leftover "OK" from the previous command can't prematurely unblock.
                        ClearReceiveBuffer()
                        ' Bug #4: serialize the write so a UI-thread click can't interleave bytes.
                        SyncLock serialLock
                            SerialPort1.Write(item.Data)
                        End SyncLock

                        ' Ghi log gửi ra RichTextBox
                        Me.Invoke(New Action(Sub()
                                                 RichTextBox2.AppendText($"Sending: {item.Data.Replace("AT+", "Vilog_")}{vbCrLf}")
                                             End Sub))

                    Catch ex As Exception
                        ' Bug #1: on a write failure, don't stay stuck waiting for an ack that
                        ' will never come.
                        IsWaitingFlag = False
                        Me.Invoke(New Action(Sub()
                                                 RichTextBox2.AppendText("Send error: " & ex.Message & vbCrLf)
                                             End Sub))
                    End Try

                    'Thread.Sleep(item.DelayMs)
                End If
            Else
            End If
            Thread.Sleep(10)
        End While
    End Sub


    Private Sub BtnSendDebugCommand_Click(sender As Object, e As EventArgs) Handles Button2.Click
        Try
            SerialPort2.Write(RichTextBox1.Text.Replace("Vilog_", "AT+") & vbCr)
        Catch ex As Exception
            RichTextBox2.AppendText("Debug send error: " & ex.Message & vbCrLf)
        End Try

        RichTextBox1.Clear()
        RichTextBox3.Clear()

    End Sub

    Private Sub Config()

        isConfiguring = True

        ' Defensive fallback for the device's ~14 buffered-samples-per-send limit.
        ' The primary enforcement is now the proactive UI filter (RebuildLogTimeItems,
        ' fired by ComboBox3/ComboBox5 SelectedIndexChanged), which prevents an invalid
        ' sendTime/logTime combo from ever being selectable. This block stays as a
        ' last-second guard for code paths that set ComboBox4 without going through the
        ' filter (e.g. the AT+CFG read-back parser in ReceivedText1).
        If ComboBox3.Text = "12h" Then
            ComboBox4.Text = "60m"
        ElseIf (ComboBox3.Text = "6h") And (ComboBox4.Text = "15m") Then
            ComboBox4.Text = "30m"
        End If

        If (ComboBox5.Text = "Aichi Tokei") Then
            Me.RichTextBox2.Text &= "Configuring logging time" & vbCr

            Dim intervalLogTime As String = "00 0F"
            Select Case ComboBox4.Text
                Case "5m"
                    intervalLogTime = "00 05"
                Case "10m"
                    intervalLogTime = "00 0A"
                Case "15m"
                    intervalLogTime = "00 0F"
                Case "30m"
                    intervalLogTime = "00 1E"
                Case "60m"
                    intervalLogTime = "00 3C"
            End Select

            Dim TS = DateTime.Now
            ' Bug #14: encode the year as a robust 2-byte (hi/lo) value using the same
            ' Right("...",2) padding convention used elsewhere, instead of relying on
            ' Hex(Year) happening to be exactly 3 digits.
            Dim yearHex As String = Microsoft.VisualBasic.Right("0000" & Hex(TS.Year), 4)
            Dim yearHi As String = yearHex.Substring(0, 2)
            Dim yearLo As String = yearHex.Substring(2, 2)
            'Set timestamp aichi
            AddToQueue("AT+CFGDEV=01 06 01 0B " & yearHi & " " & yearLo & ",1" & vbCr, 10000)
            AddToQueue("AT+CFGDEV=01 06 01 0C " & Microsoft.VisualBasic.Right("00" & Hex(TS.Month), 2) & " " & Microsoft.VisualBasic.Right("00" & Hex(TS.Day), 2) & ",1" & vbCr, 10000)
            AddToQueue("AT+CFGDEV=01 06 01 0D " & Microsoft.VisualBasic.Right("00" & Hex(TS.Hour), 2) & " " & Microsoft.VisualBasic.Right("00" & Hex(TS.Minute), 2) & " ,1" & vbCr, 10000)

            'Set start longing time aichi
            AddToQueue("AT+CFGDEV=01 06 01 01 " & yearHi & " " & yearLo & ",1" & vbCr, 10000)
            AddToQueue("AT+CFGDEV=01 06 01 02 " & Microsoft.VisualBasic.Right("00" & Hex(TS.Month), 2) & " " & Microsoft.VisualBasic.Right("00" & Hex(TS.Day), 2) & ",1" & vbCr, 10000)
            ' Bug #10: hour+1 must wrap at 23 -> 0 (Hex(24)="18" is an invalid hour byte).
            AddToQueue("AT+CFGDEV=01 06 01 03 " & Microsoft.VisualBasic.Right("00" & Hex((TS.Hour + 1) Mod 24), 2) & " 00,1" & vbCr, 10000)
            AddToQueue("AT+CFGDEV=01 06 01 04 " & intervalLogTime & ",1" & vbCr, 10000)

            Dim id As UInt16 = 1
            Dim id_hex As String
            id_hex = Hex(id)

            If Len(id_hex) < 2 Then
                id_hex = "0" & id_hex
            End If

            AddToQueue("AT+CMDEAR=1,15" & vbCr, 4000)

            AddToQueue("AT+COMMAND1=" & id_hex & " 03 01 10 00 10,1" & vbCr, 500)
            AddToQueue("AT+DATACUT1=37,2,4~35" & vbCr, 500)
            AddToQueue("AT+CMDDL1=200" & vbCr, 500)

            AddToQueue("AT+COMMAND2=" & id_hex & " 03 02 10 00 0F,1" & vbCr, 500)
            AddToQueue("AT+DATACUT2=35,2,4~33" & vbCr, 500)
            AddToQueue("AT+CMDDL2=200" & vbCr, 500)

            AddToQueue("AT+COMMAND3=" & id_hex & " 03 02 20 00 0F,1" & vbCr, 500)
            AddToQueue("AT+DATACUT3=35,2,4~33" & vbCr, 500)
            AddToQueue("AT+CMDDL3=200" & vbCr, 500)

            AddToQueue("AT+COMMAND4=" & id_hex & " 03 02 30 00 0F,1" & vbCr, 500)
            AddToQueue("AT+DATACUT4=35,2,4~33" & vbCr, 500)
            AddToQueue("AT+CMDDL4=200" & vbCr, 500)

            AddToQueue("AT+COMMAND5=" & id_hex & " 03 02 40 00 0F,1" & vbCr, 500)
            AddToQueue("AT+DATACUT5=35,2,4~33" & vbCr, 500)
            AddToQueue("AT+CMDDL5=200" & vbCr, 500)

            AddToQueue("AT+COMMAND6=" & id_hex & " 03 02 50 00 0F,1" & vbCr, 500)
            AddToQueue("AT+DATACUT6=35,2,4~33" & vbCr, 500)
            AddToQueue("AT+CMDDL6=200" & vbCr, 500)

            AddToQueue("AT+COMMAND7=" & id_hex & " 03 02 60 00 0F,1" & vbCr, 500)
            AddToQueue("AT+DATACUT7=35,2,4~33" & vbCr, 500)
            AddToQueue("AT+CMDDL7=200" & vbCr, 500)

            AddToQueue("AT+COMMAND8=" & id_hex & " 03 02 70 00 0F,1" & vbCr, 500)
            AddToQueue("AT+DATACUT8=35,2,4~33" & vbCr, 500)
            AddToQueue("AT+CMDDL8=200" & vbCr, 500)

            AddToQueue("AT+COMMAND9=" & id_hex & " 03 02 80 00 0F,1" & vbCr, 500)
            AddToQueue("AT+DATACUT9=35,2,4~33" & vbCr, 500)
            AddToQueue("AT+CMDDL9=200" & vbCr, 500)

            AddToQueue("AT+COMMANDA=" & id_hex & " 03 02 90 00 0F,1" & vbCr, 500)
            AddToQueue("AT+DATACUTA=35,2,4~33" & vbCr, 500)
            AddToQueue("AT+CMDDLA=200" & vbCr, 500)

            AddToQueue("AT+COMMANDB=" & id_hex & " 03 02 A0 00 0F,1" & vbCr, 500)
            AddToQueue("AT+DATACUTB=35,2,4~33" & vbCr, 500)
            AddToQueue("AT+CMDDLB=200" & vbCr, 500)

            AddToQueue("AT+COMMANDC=" & id_hex & " 03 02 B0 00 0F,1" & vbCr, 500)
            AddToQueue("AT+DATACUTC=35,2,4~33" & vbCr, 500)
            AddToQueue("AT+CMDDLC=200" & vbCr, 500)

            AddToQueue("AT+COMMANDD=" & id_hex & " 03 02 C0 00 0F,1" & vbCr, 500)
            AddToQueue("AT+DATACUTD=35,2,4~33" & vbCr, 500)
            AddToQueue("AT+CMDDLD=200" & vbCr, 500)

            AddToQueue("AT+COMMANDE=" & id_hex & " 03 02 D0 00 0F,1" & vbCr, 500)
            AddToQueue("AT+DATACUTE=35,2,4~33" & vbCr, 500)
            AddToQueue("AT+CMDDLE=200" & vbCr, 500)

            AddToQueue("AT+COMMANDF=" & id_hex & " 03 02 E0 00 0F,1" & vbCr, 500)
            AddToQueue("AT+DATACUTF=35,2,4~33" & vbCr, 500)
            AddToQueue("AT+CMDDLF=200" & vbCr, 500)

            Dim logTime As UInt16 = 5
            Select Case ComboBox4.Text
                Case "5m"
                    logTime = 5
                Case "10m"
                    logTime = 10
                Case "15m"
                    logTime = 15
                Case "30m"
                    logTime = 30
                Case "60m"
                    logTime = 60
            End Select
            AddToQueue("AT+CLOCKLOG=1," & logTime & ",0,0" & vbCr, 10000)
            AddToQueue("AT+PARITY=2" & vbCr, 1000)
        End If


        If (ComboBox5.Text = "Krohne") Then
            Dim id As UInt16 = 1
            Dim id_hex As String
            id_hex = Hex(id)

            If Len(id_hex) < 2 Then
                id_hex = "0" & id_hex
            End If

            AddToQueue("AT+CMDEAR=1,15" & vbCr, 4000)

            AddToQueue("AT+COMMAND1=" & id_hex & " 04 0F A2 00 02,1" & vbCr, 500)
            AddToQueue("AT+DATACUT1=9,2,4~7" & vbCr, 500)
            AddToQueue("AT+CMDDL1=200" & vbCr, 1000)

            AddToQueue("AT+COMMAND2=" & id_hex & " 04 0F A8 00 06,1" & vbCr, 500)
            AddToQueue("AT+DATACUT2=17,2,4~15" & vbCr, 500)
            AddToQueue("AT+CMDDL2=200" & vbCr, 1000)

            AddToQueue("AT+COMMAND3=" & id_hex & " 04 0F B6 00 02,1" & vbCr, 500)
            AddToQueue("AT+DATACUT3=9,2,4~7" & vbCr, 500)
            AddToQueue("AT+CMDDL3=200" & vbCr, 1000)

            AddToQueue("AT+COMMAND4=" & id_hex & " 04 0F BC 00 02,1" & vbCr, 500)
            AddToQueue("AT+DATACUT4=9,2,4~7" & vbCr, 500)
            AddToQueue("AT+CMDDL4=200" & vbCr, 1000)

            Dim sendTime As UInt16 = 10
            Select Case ComboBox3.Text
                Case "1m"
                    sendTime = 1
                Case "5m"
                    sendTime = 5
                Case "10m"
                    sendTime = 10
                Case "15m"
                    sendTime = 15
                Case "30m"
                    sendTime = 30
                Case "1h"
                    sendTime = 60
                Case "2h"
                    sendTime = 60 * 2
                Case "3h"
                    sendTime = 60 * 3
                Case "6h"
                    sendTime = 60 * 6
                Case "12h"
                    sendTime = 60 * 12
                Case "24h"
                    sendTime = 60 * 24
            End Select

            Dim logTime As UInt16 = 5
            Select Case ComboBox4.Text
                Case "5m"
                    logTime = 5
                Case "10m"
                    logTime = 10
                Case "15m"
                    logTime = 15
                Case "30m"
                    logTime = 30
                Case "60m"
                    logTime = 60
            End Select
            Dim logTimeHex As String
            logTimeHex = Hex(logTime)
            If Len(logTimeHex) < 2 Then
                logTimeHex = "0" & logTimeHex
            End If

            Dim setTime As String
            setTime = CInt(sendTime / logTime)

            AddToQueue("AT+CLOCKLOG=1,0," & logTime & "," & setTime & vbCr, 10000)
            AddToQueue("AT+PARITY=2" & vbCr, 1000)
        End If

        ' Bavitech Level Sensor:
        If (ComboBox5.Text = "Level Sensor (BVT)") Then
            Dim id As UInt16 = 1
            Dim id_hex As String
            id_hex = Hex(id)

            If Len(id_hex) < 2 Then
                id_hex = "0" & id_hex
            End If

            AddToQueue("AT+CMDEAR=1,15" & vbCr, 4000)

            AddToQueue("AT+COMMAND1=" & id_hex & " 03 00 02 00 01,1" & vbCr, 500)
            AddToQueue("AT+DATACUT1=7,2,4~5" & vbCr, 500)
            AddToQueue("AT+CMDDL1=200" & vbCr, 1000)

            Dim sendTime As UInt16 = 10
            Select Case ComboBox3.Text
                Case "1m"
                    sendTime = 1
                Case "5m"
                    sendTime = 5
                Case "10m"
                    sendTime = 10
                Case "15m"
                    sendTime = 15
                Case "30m"
                    sendTime = 30
                Case "1h"
                    sendTime = 60
                Case "2h"
                    sendTime = 60 * 2
                Case "3h"
                    sendTime = 60 * 3
                Case "6h"
                    sendTime = 60 * 6
                Case "12h"
                    sendTime = 60 * 12
                Case "24h"
                    sendTime = 60 * 24
            End Select

            Dim logTime As UInt16 = 5
            Select Case ComboBox4.Text
                Case "5m"
                    logTime = 5
                Case "10m"
                    logTime = 10
                Case "15m"
                    logTime = 15
                Case "30m"
                    logTime = 30
                Case "60m"
                    logTime = 60
            End Select
            Dim logTimeHex As String
            logTimeHex = Hex(logTime)
            If Len(logTimeHex) < 2 Then
                logTimeHex = "0" & logTimeHex
            End If

            Dim setTime As String
            setTime = CInt(sendTime / logTime)

            AddToQueue("AT+CLOCKLOG=1,0," & logTime & "," & setTime & vbCr, 10000)
            AddToQueue("AT+PARITY=0" & vbCr, 1000)
        End If


        If ComboBox3.Text <> Nothing Then
            If ComboBox3.Text = "1m" Then
                AddToQueue("AT+TDC=60" & vbCr, 5000)
            ElseIf ComboBox3.Text = "5m" Then
                AddToQueue("AT+TDC=300" & vbCr, 5000)
            ElseIf ComboBox3.Text = "10m" Then
                AddToQueue("AT+TDC=600" & vbCr, 5000)
            ElseIf ComboBox3.Text = "15m" Then
                AddToQueue("AT+TDC=900" & vbCr, 5000)
            ElseIf ComboBox3.Text = "30m" Then
                AddToQueue("AT+TDC=1800" & vbCr, 5000)
            ElseIf ComboBox3.Text = "1h" Then
                AddToQueue("AT+TDC=3600" & vbCr, 5000)
            ElseIf ComboBox3.Text = "2h" Then
                AddToQueue("AT+TDC=7200" & vbCr, 5000)
            ElseIf ComboBox3.Text = "3h" Then
                AddToQueue("AT+TDC=10800" & vbCr, 5000)
            ElseIf ComboBox3.Text = "4h" Then
                AddToQueue("AT+TDC=14400" & vbCr, 5000)
            ElseIf ComboBox3.Text = "6h" Then

                AddToQueue("AT+TDC=21600" & vbCr, 5000)
            ElseIf ComboBox3.Text = "12h" Then
                AddToQueue("AT+TDC=43200" & vbCr, 5000)
            ElseIf ComboBox3.Text = "24h" Then
                AddToQueue("AT+TDC=86400" & vbCr, 5000)
            End If
        End If

        'AddToQueue("AT+SERVADDR=157.66.81.22,1883" & vbCr, 5000)
        AddToQueue("AT+SERVADDR=43.216.183.205,1883" & vbCr, 5000)

        AddToQueue("AT+5VT=1000" & vbCr, 1000)
        AddToQueue("AT+BAUDR=9600" & vbCr, 1000)

        AddToQueue("AT+MBFUN=1" & vbCr, 1000)

        AddToQueue("AT+SUBTOPIC=Vilog_" & TextBox12.Text & "_" & TextBox11.Text & "_SUB" & vbCr, 10000)
        AddToQueue("AT+PUBTOPIC=Vilog_" & TextBox12.Text & "_" & TextBox11.Text & "_PUB" & vbCr, 10000)

        'AddToQueue("AT+GETSENSORVALUE=1" & vbCr, 1000)



    End Sub

    ' --- sendTime/logTime dependency & device sample-count limit -------------------
    ' The Dragino data-logger can only buffer ~14 log samples between two send cycles.
    ' setTime = CInt(sendTime / logTime) (see Krohne / Level Sensor branches in Config())
    ' is that sample count, so any combo where sendTime/logTime > 14 is invalid.
    ' Instead of silently overriding logTime at Config() time, we filter the logTime
    ' dropdown (ComboBox4) whenever the sendTime (ComboBox3) or sensor (ComboBox5)
    ' selection changes, so only valid options are ever selectable.

    ' ComboBox3 (sendTime) label -> minutes. Mirrors the AT+TDC / sendTime tables in
    ' Config(); kept as a single source of truth for the filtering logic.
    Private Function SendTimeToMinutes(ByVal label As String) As Integer
        Select Case label
            Case "1m" : Return 1
            Case "5m" : Return 5
            Case "10m" : Return 10
            Case "15m" : Return 15
            Case "30m" : Return 30
            Case "1h" : Return 60
            Case "2h" : Return 120
            Case "3h" : Return 180
            Case "4h" : Return 240
            Case "6h" : Return 360
            Case "12h" : Return 720
            Case "24h" : Return 1440
            Case Else : Return 0
        End Select
    End Function

    ' ComboBox4 (logTime) label -> minutes.
    Private Function LogTimeToMinutes(ByVal label As String) As Integer
        Select Case label
            Case "5m" : Return 5
            Case "10m" : Return 10
            Case "15m" : Return 15
            Case "30m" : Return 30
            Case "60m" : Return 60
            Case Else : Return 0
        End Select
    End Function

    ' logTime options each sensor can encode. All sensors (Aichi Tokei, Krohne, and the
    ' Bavitech Level Sensor (BVT)) share the same 15/30/60m set. Aichi Tokei's
    ' intervalLogTime is a plain 2-byte hex encoding of the raw minute value
    ' (15=00 0F, 30=00 1E, 60=00 3C) in Config(), so it is not restricted to a lookup
    ' table; the set is intentionally identical across sensors.
    Private Function LogTimeCandidatesForSensor() As String()
        Return New String() {"15m", "30m", "60m"}
    End Function

    ' Rebuild ComboBox4 (logTime) so it only offers options valid for the current
    ' sendTime (ComboBox3) and sensor (ComboBox5): keep logTime where
    ' CInt(sendTime / logTime) <= 14 (same division used to compute setTime in Config()).
    Private Sub RebuildLogTimeItems()
        Dim sendMin As Integer = SendTimeToMinutes(ComboBox3.Text)
        Dim candidates() As String = LogTimeCandidatesForSensor()
        Dim prev As String = ComboBox4.Text

        Dim allowed As New System.Collections.Generic.List(Of String)
        If sendMin <= 0 Then
            ' No sendTime chosen yet: show the full sensor-supported set unfiltered.
            allowed.AddRange(candidates)
        Else
            For Each c As String In candidates
                Dim logMin As Integer = LogTimeToMinutes(c)
                If logMin > 0 AndAlso CInt(sendMin / logMin) <= 14 Then
                    allowed.Add(c)
                End If
            Next
            ' Never leave the dropdown empty. The largest logTime minimizes the ratio,
            ' so fall back to the last candidate (60m) if nothing passed (e.g. 24h).
            If allowed.Count = 0 Then
                allowed.Add(candidates(candidates.Length - 1))
            End If
        End If

        ComboBox4.BeginUpdate()
        ComboBox4.Items.Clear()
        ComboBox4.Items.AddRange(allowed.ToArray())
        ComboBox4.EndUpdate()

        ' Preserve the previous selection if it survived the filter; otherwise default
        ' to the first (smallest) valid logTime.
        If Not String.IsNullOrEmpty(prev) AndAlso allowed.Contains(prev) Then
            ComboBox4.SelectedItem = prev
        ElseIf ComboBox4.Items.Count > 0 Then
            ComboBox4.SelectedIndex = 0
        End If
    End Sub

    Private Sub ComboBox3_SelectedIndexChanged(sender As Object, e As EventArgs) Handles ComboBox3.SelectedIndexChanged
        RebuildLogTimeItems()
        ' Gate logTime on sendTime being chosen. On the AT+CFG read-back path
        ' (ProcessLoggerResponse) ComboBox3 is set programmatically before ComboBox4, so this
        ' fires first and re-enables ComboBox4 in time for the CLOCKLOG branch's assignment.
        ComboBox4.Enabled = (ComboBox3.SelectedIndex >= 0 AndAlso ComboBox3.Text <> "")
    End Sub

    Private Sub ComboBox5_SelectedIndexChanged(sender As Object, e As EventArgs) Handles ComboBox5.SelectedIndexChanged
        ' Sensor change can change which logTime options are encodable, so re-filter.
        RebuildLogTimeItems()
    End Sub

    Private Sub BtnRefreshDebugPorts_Click(sender As Object, e As EventArgs) Handles Button6.Click
        myPort = IO.Ports.SerialPort.GetPortNames()
        ComboBox2.Items.Clear()
        ComboBox2.Items.AddRange(myPort)
    End Sub

    Private Sub BtnOpenDebugPort_Click(sender As Object, e As EventArgs) Handles Button8.Click
        If (ComboBox2.Text <> Nothing) Then
            SerialPort2.Close()

            SerialPort2.PortName = ComboBox2.Text
            SerialPort2.BaudRate = 9600
            ' Review Finding 1: finite write/read timeout so a stalled port can't block.
            SerialPort2.WriteTimeout = 2000
            SerialPort2.ReadTimeout = 2000

            Try
                SerialPort2.Open()
                Button8.Enabled = False
                Button7.Enabled = True
            Catch ex As Exception
                SerialPort2.Close()
                RichTextBox2.AppendText("Debug port open error: " & ex.Message & vbCrLf)
                GoTo cannot_connect
            End Try

cannot_connect:
        End If
    End Sub

    Private Sub BtnCloseDebugPort_Click(sender As Object, e As EventArgs) Handles Button7.Click
        SerialPort2.Close()
        Button8.Enabled = True
        Button7.Enabled = False

    End Sub

    Private Sub DebugCommandInput_KeyDown(sender As Object, e As KeyEventArgs) Handles RichTextBox1.KeyDown
        If e.KeyCode = Keys.Enter Then
            Try
                SerialPort2.Write(RichTextBox1.Text.Replace("Vilog_", "AT+") & vbCr)
            Catch ex As Exception
                RichTextBox2.AppendText("Debug send error: " & ex.Message & vbCrLf)
            End Try
            RichTextBox3.Clear()
            RichTextBox1.Clear()
        End If
    End Sub


    Private Sub SerialPortMonitor_DataReceived(sender As Object, e As SerialDataReceivedEventArgs) Handles SerialPort3.DataReceived
        ProcessMonitorResponse(SerialPort3.ReadExisting())
    End Sub


    Private Sub BtnBrowseDebugFolder_Click(sender As Object, e As EventArgs) Handles Button10.Click
        If (FolderBrowserDialog1.ShowDialog() = DialogResult.OK) Then
            TextBox1.Text = FolderBrowserDialog1.SelectedPath
        End If

    End Sub

    Private Sub BtnReadDebugFile_Click(sender As Object, e As EventArgs) Handles Button3.Click
        RichTextBox2.Clear()
        'RichTextBox4.Text = "Reading Debug File, Please wait,..."
        RichTextBox4.Clear()
        debugFileContent = ""
        ' Bug #4: serialize the write. Bug #15: the response arrives asynchronously via
        ' DataReceived, so the blocking UI-thread Thread.Sleep(5000) is unnecessary and
        ' only froze the UI; removed. Finding 1: catch a WriteTimeout so it doesn't surface
        ' as an unhandled UI exception (SyncLock still releases the lock on the way out).
        Try
            SyncLock serialLock
                SerialPort1.Write("AT+CFG " & vbCr)
            End SyncLock
        Catch ex As Exception
            RichTextBox2.AppendText("Write error: " & ex.Message & vbCrLf)
        End Try
    End Sub

    Private Sub TabControl1_SelectedIndexChanged(sender As Object, e As EventArgs) Handles TabControl1.SelectedIndexChanged
        RichTextBox4.Clear()
    End Sub

    Private Sub BtnSaveDebugFile_Click(sender As Object, e As EventArgs) Handles Button11.Click
        If TextBox1.Text.Length > 1 Then
            If (debugFileContent.Length > 1) Then

                If debugFileContent.Contains("AT+") Then
                    debugFileContent = debugFileContent.Replace("AT+", "Vilog_")
                End If
                Dim currentDateTime As String = Date.Now.ToString("yyyyMMddHHmmss")
                Dim fileName As String = TextBox1.Text & "\DebugFile_" & currentDateTime & ".txt"
                Dim sw As New StreamWriter(fileName, True)
                Try
                    sw.WriteLine(debugFileContent)
                Catch ex As Exception
                    RichTextBox2.AppendText("Debug file save error: " & ex.Message & vbCrLf)
                Finally
                    sw.Close()
                End Try
            End If
        Else
            RichTextBox4.Text = "Please enter folder path"
        End If
    End Sub

    Private Sub BtnLoadParameters_Click(sender As Object, e As EventArgs) Handles Button12.Click
        ' Append status so it adds a line into the ongoing raw log instead of erasing it.
        Me.RichTextBox2.AppendText(vbCr & "Loading parameter ..." & vbCr)
        DeviceParameterString = ""
        isReadingParameter = True
        ' Bug #4: serialize the write. Bug #15: parsing happens asynchronously in
        ' ProcessLoggerResponse (isReadingParameter branch, cleared on "OK"), so the blocking
        ' UI-thread Thread.Sleep(5000) is unnecessary and only froze the UI; removed.
        ' Finding 1: catch a WriteTimeout so it doesn't surface as an unhandled UI exception.
        Try
            SyncLock serialLock
                SerialPort1.Write("AT+CFG" & vbCr)
            End SyncLock
        Catch ex As Exception
            RichTextBox2.AppendText("Write error: " & ex.Message & vbCrLf)
        End Try
    End Sub

    Private Sub BtnGetSensorValue_Click(sender As Object, e As EventArgs) Handles Button13.Click
        RichTextBox2.Clear()
        RichTextBox4.Clear()
        ' Bug #4: serialize the write. Finding 1: catch a WriteTimeout.
        Try
            SyncLock serialLock
                SerialPort1.Write("AT+GETSENSORVALUE=1" & vbCr)
            End SyncLock
        Catch ex As Exception
            RichTextBox2.AppendText("Write error: " & ex.Message & vbCrLf)
        End Try
    End Sub

    Private Sub AddToQueue(data As String, delayMs As Integer)
        SyncLock sendQueue
            sendQueue.Enqueue(New SendItem With {.Data = data, .DelayMs = delayMs})
        End SyncLock
    End Sub

    Private Sub BtnStopLogger_Click(sender As Object, e As EventArgs) Handles Button14.Click
        RichTextBox2.Clear()
        RichTextBox4.Clear()
        ' Bug #4: serialize the write. Finding 1: catch a WriteTimeout.
        Try
            SyncLock serialLock
                SerialPort1.Write("AT+TDC=8640000" & vbCr)
            End SyncLock
            ' Append status so it adds a line into the ongoing raw log instead of erasing it.
            Me.RichTextBox2.AppendText(vbCr & "Vilog is Stopped" & vbCr)
        Catch ex As Exception
            RichTextBox2.AppendText("Write error: " & ex.Message & vbCrLf)
        End Try
    End Sub

    Private Sub BtnSendData_Click(sender As Object, e As EventArgs) Handles Button15.Click
        RichTextBox2.Clear()
        RichTextBox4.Clear()
        ' Bug #4: serialize the write. Finding 1: catch a WriteTimeout.
        Try
            SyncLock serialLock
                SerialPort1.Write("AT+GETSENSORVALUE=1" & vbCr)
            End SyncLock
            ' Append status so it adds a line into the ongoing raw log instead of erasing it.
            Me.RichTextBox2.AppendText(vbCr & "Send Data command sent" & vbCr)
        Catch ex As Exception
            RichTextBox2.AppendText("Write error: " & ex.Message & vbCrLf)
        End Try
    End Sub

    ' Bug #7: close any open serial ports (and stop the send loop) when the form closes,
    ' so ports aren't left locked/open after exit.
    Private Sub Form1_FormClosing(sender As Object, e As FormClosingEventArgs) Handles MyBase.FormClosing
        Try
            SendingFlag = False
        Catch
        End Try
        Try
            If SerialPort1.IsOpen Then SerialPort1.Close()
        Catch
        End Try
        Try
            If SerialPort2.IsOpen Then SerialPort2.Close()
        Catch
        End Try
        Try
            If SerialPort3.IsOpen Then SerialPort3.Close()
        Catch
        End Try
    End Sub
End Class
