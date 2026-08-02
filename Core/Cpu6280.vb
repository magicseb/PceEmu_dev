''' <summary>CPU HuC6280 (65C02 modifié) - implémentation complète</summary>
Public Class Cpu6280

    ' ===== Registres (Integer en interne pour éviter les overflows VB) =====
    Public A As Integer = 0
    Public X As Integer = 0
    Public Y As Integer = 0
    Public S As Integer = &HFF
    Public PC As Integer = 0
    Public P As Integer = FLAG_I

    ' Flags
    Public Const FLAG_C As Integer = &H1
    Public Const FLAG_Z As Integer = &H2
    Public Const FLAG_I As Integer = &H4
    Public Const FLAG_D As Integer = &H8
    Public Const FLAG_B As Integer = &H10
    Public Const FLAG_T As Integer = &H20
    Public Const FLAG_V As Integer = &H40
    Public Const FLAG_N As Integer = &H80

    Public CyclesThisFrame As Long = 0
    Public Halted As Boolean = False
    Public FastMode As Boolean = True

    Private mpu As MemoryMap
    Private vdc As Vdc

    ' Flag T : actif pour la prochaine instruction
    Private tFlagPending As Boolean = False
    Private tFlagActive As Boolean = False

    Public Sub New(memory As MemoryMap, vdcRef As Vdc)
        mpu = memory
        vdc = vdcRef
        Reset()
    End Sub

    ''' <summary>Réinitialise le CPU (vecteur RESET = $FFFE)</summary>
    Public Sub Reset()
        A = 0 : X = 0 : Y = 0
        S = &HFF
        P = FLAG_I Or FLAG_B
        CyclesThisFrame = 0
        Halted = False
        FastMode = False
        tFlagPending = False
        tFlagActive = False
        ' Le HuC6280 démarre avec MPR7 = 0
        PC = ReadMem(&HFFFE) Or (ReadMem(&HFFFF) << 8)
    End Sub

    ''' <summary>Vérifie les interruptions (appelé avant chaque instruction)</summary>
    Private Function CheckInterrupts() As Boolean
        ' Délai d'un cran après un démasquage d'IRQ ($1402) : l'IRQ ré-autorisée
        ' n'est reconnue qu'à l'instruction suivante, ce qui laisse l'instruction
        ' d'acquittement s'exécuter (évite la ré-entrance des handlers timer).
        If mpu.IrqEnableDelay Then
            mpu.IrqEnableDelay = False
            Return False
        End If
        If (P And FLAG_I) <> 0 Then Return False
        Dim disable = mpu.IrqDisable

        ' IRQ2 - CD-ROM² (vecteur $FFF6, priorité la plus haute)
        If mpu.Irq2Line AndAlso (disable And &H1) = 0 Then
            DoInterrupt(&HFFF6)
            Return True
        End If

        ' TIMER (vecteur $FFFA)
        If mpu.TimerRef IsNot Nothing AndAlso mpu.TimerRef.IrqPending AndAlso (disable And &H4) = 0 Then
            DoInterrupt(&HFFFA)
            Return True
        End If
        ' IRQ1 - VDC (vecteur $FFF8)
        If mpu.Irq1Line AndAlso (disable And &H2) = 0 Then
            DoInterrupt(&HFFF8)
            Return True
        End If
        Return False
    End Function

    Private Sub DoInterrupt(vector As Integer)
        PushByte((PC >> 8) And &HFF)
        PushByte(PC And &HFF)
        PushByte(P And Not (FLAG_B Or FLAG_T))
        P = (P Or FLAG_I) And Not (FLAG_D Or FLAG_T)
        tFlagPending = False : tFlagActive = False
        PC = ReadMem(vector) Or (ReadMem(vector + 1) << 8)
    End Sub

    ''' <summary>Exécute une instruction, retourne les cycles consommés</summary>
    Public Function ExecuteInstruction() As Integer
        If Halted Then Return 2

        If CheckInterrupts() Then
            CyclesThisFrame += 8
            Return 8
        End If

        ' Gestion flag T (SET affecte l'instruction suivante)
        tFlagActive = tFlagPending
        tFlagPending = False

        Dim opcode = Fetch()
        Dim cycles = ExecuteOpcode(opcode)
        CyclesThisFrame += cycles
        Return cycles
    End Function

    ' ===== Accès mémoire =====
    Private Function ReadMem(addr As Integer) As Integer
        Return mpu.ReadByte(addr And &HFFFF)
    End Function

    Private Sub WriteMem(addr As Integer, val As Integer)
        mpu.WriteByte(addr And &HFFFF, val And &HFF)
    End Sub

    Private Function Fetch() As Integer
        Dim v = ReadMem(PC)
        PC = (PC + 1) And &HFFFF
        Return v
    End Function

    Private Function FetchWord() As Integer
        Dim lo = Fetch()
        Dim hi = Fetch()
        Return lo Or (hi << 8)
    End Function

    ' Zéro page : adresse logique $2000 + zp (mappée par MPR1)
    Private Function ZpAddr(zp As Integer) As Integer
        Return &H2000 Or (zp And &HFF)
    End Function

    Private Function ReadZp(zp As Integer) As Integer
        Return ReadMem(ZpAddr(zp))
    End Function

    Private Sub WriteZp(zp As Integer, val As Integer)
        WriteMem(ZpAddr(zp), val)
    End Sub

    ' Pile : $2100 + S
    Private Sub PushByte(val As Integer)
        WriteMem(&H2100 Or (S And &HFF), val)
        S = (S - 1) And &HFF
    End Sub

    Private Function PopByte() As Integer
        S = (S + 1) And &HFF
        Return ReadMem(&H2100 Or (S And &HFF))
    End Function

    ' ===== Modes d'adressage (retournent l'adresse effective) =====
    Private Function AddrZp() As Integer
        Return ZpAddr(Fetch())
    End Function

    Private Function AddrZpX() As Integer
        Return ZpAddr(Fetch() + X)
    End Function

    Private Function AddrZpY() As Integer
        Return ZpAddr(Fetch() + Y)
    End Function

    Private Function AddrAbs() As Integer
        Return FetchWord()
    End Function

    Private Function AddrAbsX() As Integer
        Return (FetchWord() + X) And &HFFFF
    End Function

    Private Function AddrAbsY() As Integer
        Return (FetchWord() + Y) And &HFFFF
    End Function

    Private Function AddrIndX() As Integer  ' (zp,X)
        Dim zp = Fetch() + X
        Return ReadZp(zp) Or (ReadZp(zp + 1) << 8)
    End Function

    Private Function AddrIndY() As Integer  ' (zp),Y
        Dim zp = Fetch()
        Dim base = ReadZp(zp) Or (ReadZp(zp + 1) << 8)
        Return (base + Y) And &HFFFF
    End Function

    Private Function AddrInd() As Integer   ' (zp)
        Dim zp = Fetch()
        Return ReadZp(zp) Or (ReadZp(zp + 1) << 8)
    End Function

    ' ===== Helpers flags =====
    Private Sub SetFlag(flag As Integer, cond As Boolean)
        If cond Then P = P Or flag Else P = P And Not flag
    End Sub

    Private Sub UpdateZN(val As Integer)
        SetFlag(FLAG_Z, (val And &HFF) = 0)
        SetFlag(FLAG_N, (val And &H80) <> 0)
    End Sub

    ' ===== Opérations ALU (avec support flag T) =====
    Private Sub DoADC(operand As Integer)
        If tFlagActive Then
            Dim zaddr = ZpAddr(X)
            Dim m = ReadMem(zaddr)
            Dim r = AdcCalc(m, operand)
            WriteMem(zaddr, r)
            UpdateZN(r)
        Else
            A = AdcCalc(A, operand)
            UpdateZN(A)
        End If
    End Sub

    Private Function AdcCalc(acc As Integer, operand As Integer) As Integer
        Dim carry = If((P And FLAG_C) <> 0, 1, 0)
        If (P And FLAG_D) <> 0 Then
            ' Mode BCD
            Dim lo = (acc And &HF) + (operand And &HF) + carry
            Dim hi = (acc >> 4) + (operand >> 4)
            If lo > 9 Then lo += 6 : hi += 1
            SetFlag(FLAG_V, False)
            If hi > 9 Then hi += 6
            SetFlag(FLAG_C, hi > 15)
            Return ((hi And &HF) << 4) Or (lo And &HF)
        Else
            Dim sum = acc + operand + carry
            SetFlag(FLAG_C, sum > &HFF)
            SetFlag(FLAG_V, ((acc Xor sum) And (operand Xor sum) And &H80) <> 0)
            Return sum And &HFF
        End If
    End Function

    Private Sub DoSBC(operand As Integer)
        Dim carry = If((P And FLAG_C) <> 0, 0, 1)
        If (P And FLAG_D) <> 0 Then
            Dim lo = (A And &HF) - (operand And &HF) - carry
            Dim hi = (A >> 4) - (operand >> 4)
            If lo < 0 Then lo -= 6 : hi -= 1
            If hi < 0 Then hi -= 6
            Dim result = A - operand - carry
            SetFlag(FLAG_C, result >= 0)
            A = ((hi And &HF) << 4) Or (lo And &HF)
            UpdateZN(A)
        Else
            Dim diff = A - operand - carry
            SetFlag(FLAG_C, diff >= 0)
            SetFlag(FLAG_V, ((A Xor operand) And (A Xor diff) And &H80) <> 0)
            A = diff And &HFF
            UpdateZN(A)
        End If
    End Sub

    Private Sub DoAND(operand As Integer)
        If tFlagActive Then
            Dim zaddr = ZpAddr(X)
            Dim r = ReadMem(zaddr) And operand
            WriteMem(zaddr, r)
            UpdateZN(r)
        Else
            A = A And operand
            UpdateZN(A)
        End If
    End Sub

    Private Sub DoORA(operand As Integer)
        If tFlagActive Then
            Dim zaddr = ZpAddr(X)
            Dim r = ReadMem(zaddr) Or operand
            WriteMem(zaddr, r)
            UpdateZN(r)
        Else
            A = A Or operand
            UpdateZN(A)
        End If
    End Sub

    Private Sub DoEOR(operand As Integer)
        If tFlagActive Then
            Dim zaddr = ZpAddr(X)
            Dim r = ReadMem(zaddr) Xor operand
            WriteMem(zaddr, r)
            UpdateZN(r)
        Else
            A = A Xor operand
            UpdateZN(A)
        End If
    End Sub

    Private Sub DoCMP(reg As Integer, operand As Integer)
        Dim r = reg - operand
        SetFlag(FLAG_C, r >= 0)
        UpdateZN(r And &HFF)
    End Sub

    Private Function DoASL(val As Integer) As Integer
        SetFlag(FLAG_C, (val And &H80) <> 0)
        Dim r = (val << 1) And &HFF
        UpdateZN(r)
        Return r
    End Function

    Private Function DoLSR(val As Integer) As Integer
        SetFlag(FLAG_C, (val And 1) <> 0)
        Dim r = (val >> 1) And &H7F
        UpdateZN(r)
        Return r
    End Function

    Private Function DoROL(val As Integer) As Integer
        Dim c = If((P And FLAG_C) <> 0, 1, 0)
        SetFlag(FLAG_C, (val And &H80) <> 0)
        Dim r = ((val << 1) Or c) And &HFF
        UpdateZN(r)
        Return r
    End Function

    Private Function DoROR(val As Integer) As Integer
        Dim c = If((P And FLAG_C) <> 0, &H80, 0)
        SetFlag(FLAG_C, (val And 1) <> 0)
        Dim r = ((val >> 1) Or c) And &HFF
        UpdateZN(r)
        Return r
    End Function

    Private Sub DoBIT(operand As Integer)
        SetFlag(FLAG_Z, (A And operand) = 0)
        SetFlag(FLAG_N, (operand And &H80) <> 0)
        SetFlag(FLAG_V, (operand And &H40) <> 0)
    End Sub

    Private Sub Branch(cond As Boolean)
        Dim rel = Fetch()
        If cond Then
            If rel >= &H80 Then rel -= 256
            PC = (PC + rel) And &HFFFF
        End If
    End Sub

    ' ===== Décodage principal =====
    Private Function ExecuteOpcode(op As Integer) As Integer
        Select Case op
            ' --- BRK / interruptions logicielles ---
            Case &H0  ' BRK (vecteur IRQ2 $FFF6)
                PC = (PC + 1) And &HFFFF
                PushByte((PC >> 8) And &HFF)
                PushByte(PC And &HFF)
                PushByte(P Or FLAG_B)
                P = (P Or FLAG_I) And Not (FLAG_D Or FLAG_T)
                PC = ReadMem(&HFFF6) Or (ReadMem(&HFFF7) << 8)
                Return 8

            ' --- ORA ---
            Case &H1 : DoORA(ReadMem(AddrIndX())) : Return 7
            Case &H5 : DoORA(ReadMem(AddrZp())) : Return 4
            Case &H9 : DoORA(Fetch()) : Return 2
            Case &HD : DoORA(ReadMem(AddrAbs())) : Return 5
            Case &H11 : DoORA(ReadMem(AddrIndY())) : Return 7
            Case &H12 : DoORA(ReadMem(AddrInd())) : Return 7
            Case &H15 : DoORA(ReadMem(AddrZpX())) : Return 4
            Case &H19 : DoORA(ReadMem(AddrAbsY())) : Return 5
            Case &H1D : DoORA(ReadMem(AddrAbsX())) : Return 5

            ' --- AND ---
            Case &H21 : DoAND(ReadMem(AddrIndX())) : Return 7
            Case &H25 : DoAND(ReadMem(AddrZp())) : Return 4
            Case &H29 : DoAND(Fetch()) : Return 2
            Case &H2D : DoAND(ReadMem(AddrAbs())) : Return 5
            Case &H31 : DoAND(ReadMem(AddrIndY())) : Return 7
            Case &H32 : DoAND(ReadMem(AddrInd())) : Return 7
            Case &H35 : DoAND(ReadMem(AddrZpX())) : Return 4
            Case &H39 : DoAND(ReadMem(AddrAbsY())) : Return 5
            Case &H3D : DoAND(ReadMem(AddrAbsX())) : Return 5

            ' --- EOR ---
            Case &H41 : DoEOR(ReadMem(AddrIndX())) : Return 7
            Case &H45 : DoEOR(ReadMem(AddrZp())) : Return 4
            Case &H49 : DoEOR(Fetch()) : Return 2
            Case &H4D : DoEOR(ReadMem(AddrAbs())) : Return 5
            Case &H51 : DoEOR(ReadMem(AddrIndY())) : Return 7
            Case &H52 : DoEOR(ReadMem(AddrInd())) : Return 7
            Case &H55 : DoEOR(ReadMem(AddrZpX())) : Return 4
            Case &H59 : DoEOR(ReadMem(AddrAbsY())) : Return 5
            Case &H5D : DoEOR(ReadMem(AddrAbsX())) : Return 5

            ' --- ADC ---
            Case &H61 : DoADC(ReadMem(AddrIndX())) : Return 7
            Case &H65 : DoADC(ReadMem(AddrZp())) : Return 4
            Case &H69 : DoADC(Fetch()) : Return 2
            Case &H6D : DoADC(ReadMem(AddrAbs())) : Return 5
            Case &H71 : DoADC(ReadMem(AddrIndY())) : Return 7
            Case &H72 : DoADC(ReadMem(AddrInd())) : Return 7
            Case &H75 : DoADC(ReadMem(AddrZpX())) : Return 4
            Case &H79 : DoADC(ReadMem(AddrAbsY())) : Return 5
            Case &H7D : DoADC(ReadMem(AddrAbsX())) : Return 5

            ' --- SBC ---
            Case &HE1 : DoSBC(ReadMem(AddrIndX())) : Return 7
            Case &HE5 : DoSBC(ReadMem(AddrZp())) : Return 4
            Case &HE9 : DoSBC(Fetch()) : Return 2
            Case &HED : DoSBC(ReadMem(AddrAbs())) : Return 5
            Case &HF1 : DoSBC(ReadMem(AddrIndY())) : Return 7
            Case &HF2 : DoSBC(ReadMem(AddrInd())) : Return 7
            Case &HF5 : DoSBC(ReadMem(AddrZpX())) : Return 4
            Case &HF9 : DoSBC(ReadMem(AddrAbsY())) : Return 5
            Case &HFD : DoSBC(ReadMem(AddrAbsX())) : Return 5

            ' --- CMP / CPX / CPY ---
            Case &HC1 : DoCMP(A, ReadMem(AddrIndX())) : Return 7
            Case &HC5 : DoCMP(A, ReadMem(AddrZp())) : Return 4
            Case &HC9 : DoCMP(A, Fetch()) : Return 2
            Case &HCD : DoCMP(A, ReadMem(AddrAbs())) : Return 5
            Case &HD1 : DoCMP(A, ReadMem(AddrIndY())) : Return 7
            Case &HD2 : DoCMP(A, ReadMem(AddrInd())) : Return 7
            Case &HD5 : DoCMP(A, ReadMem(AddrZpX())) : Return 4
            Case &HD9 : DoCMP(A, ReadMem(AddrAbsY())) : Return 5
            Case &HDD : DoCMP(A, ReadMem(AddrAbsX())) : Return 5
            Case &HE0 : DoCMP(X, Fetch()) : Return 2
            Case &HE4 : DoCMP(X, ReadMem(AddrZp())) : Return 4
            Case &HEC : DoCMP(X, ReadMem(AddrAbs())) : Return 5
            Case &HC0 : DoCMP(Y, Fetch()) : Return 2
            Case &HC4 : DoCMP(Y, ReadMem(AddrZp())) : Return 4
            Case &HCC : DoCMP(Y, ReadMem(AddrAbs())) : Return 5

            ' --- LDA / LDX / LDY ---
            Case &HA1 : A = ReadMem(AddrIndX()) : UpdateZN(A) : Return 7
            Case &HA5 : A = ReadMem(AddrZp()) : UpdateZN(A) : Return 4
            Case &HA9 : A = Fetch() : UpdateZN(A) : Return 2
            Case &HAD : A = ReadMem(AddrAbs()) : UpdateZN(A) : Return 5
            Case &HB1 : A = ReadMem(AddrIndY()) : UpdateZN(A) : Return 7
            Case &HB2 : A = ReadMem(AddrInd()) : UpdateZN(A) : Return 7
            Case &HB5 : A = ReadMem(AddrZpX()) : UpdateZN(A) : Return 4
            Case &HB9 : A = ReadMem(AddrAbsY()) : UpdateZN(A) : Return 5
            Case &HBD : A = ReadMem(AddrAbsX()) : UpdateZN(A) : Return 5
            Case &HA2 : X = Fetch() : UpdateZN(X) : Return 2
            Case &HA6 : X = ReadMem(AddrZp()) : UpdateZN(X) : Return 4
            Case &HAE : X = ReadMem(AddrAbs()) : UpdateZN(X) : Return 5
            Case &HB6 : X = ReadMem(AddrZpY()) : UpdateZN(X) : Return 4
            Case &HBE : X = ReadMem(AddrAbsY()) : UpdateZN(X) : Return 5
            Case &HA0 : Y = Fetch() : UpdateZN(Y) : Return 2
            Case &HA4 : Y = ReadMem(AddrZp()) : UpdateZN(Y) : Return 4
            Case &HAC : Y = ReadMem(AddrAbs()) : UpdateZN(Y) : Return 5
            Case &HB4 : Y = ReadMem(AddrZpX()) : UpdateZN(Y) : Return 4
            Case &HBC : Y = ReadMem(AddrAbsX()) : UpdateZN(Y) : Return 5

            ' --- STA / STX / STY / STZ ---
            Case &H81 : WriteMem(AddrIndX(), A) : Return 7
            Case &H85 : WriteMem(AddrZp(), A) : Return 4
            Case &H8D : WriteMem(AddrAbs(), A) : Return 5
            Case &H91 : WriteMem(AddrIndY(), A) : Return 7
            Case &H92 : WriteMem(AddrInd(), A) : Return 7
            Case &H95 : WriteMem(AddrZpX(), A) : Return 4
            Case &H99 : WriteMem(AddrAbsY(), A) : Return 5
            Case &H9D : WriteMem(AddrAbsX(), A) : Return 5
            Case &H86 : WriteMem(AddrZp(), X) : Return 4
            Case &H8E : WriteMem(AddrAbs(), X) : Return 5
            Case &H96 : WriteMem(AddrZpY(), X) : Return 4
            Case &H84 : WriteMem(AddrZp(), Y) : Return 4
            Case &H8C : WriteMem(AddrAbs(), Y) : Return 5
            Case &H94 : WriteMem(AddrZpX(), Y) : Return 4
            Case &H64 : WriteMem(AddrZp(), 0) : Return 4
            Case &H74 : WriteMem(AddrZpX(), 0) : Return 4
            Case &H9C : WriteMem(AddrAbs(), 0) : Return 5
            Case &H9E : WriteMem(AddrAbsX(), 0) : Return 5

            ' --- INC/DEC ---
            Case &H1A : A = (A + 1) And &HFF : UpdateZN(A) : Return 2
            Case &H3A : A = (A - 1) And &HFF : UpdateZN(A) : Return 2
            Case &HE6 : Dim a1 = AddrZp() : Dim v1 = (ReadMem(a1) + 1) And &HFF : WriteMem(a1, v1) : UpdateZN(v1) : Return 6
            Case &HF6 : Dim a2 = AddrZpX() : Dim v2 = (ReadMem(a2) + 1) And &HFF : WriteMem(a2, v2) : UpdateZN(v2) : Return 6
            Case &HEE : Dim a3 = AddrAbs() : Dim v3 = (ReadMem(a3) + 1) And &HFF : WriteMem(a3, v3) : UpdateZN(v3) : Return 7
            Case &HFE : Dim a4 = AddrAbsX() : Dim v4 = (ReadMem(a4) + 1) And &HFF : WriteMem(a4, v4) : UpdateZN(v4) : Return 7
            Case &HC6 : Dim a5 = AddrZp() : Dim v5 = (ReadMem(a5) - 1) And &HFF : WriteMem(a5, v5) : UpdateZN(v5) : Return 6
            Case &HD6 : Dim a6 = AddrZpX() : Dim v6 = (ReadMem(a6) - 1) And &HFF : WriteMem(a6, v6) : UpdateZN(v6) : Return 6
            Case &HCE : Dim a7 = AddrAbs() : Dim v7 = (ReadMem(a7) - 1) And &HFF : WriteMem(a7, v7) : UpdateZN(v7) : Return 7
            Case &HDE : Dim a8 = AddrAbsX() : Dim v8 = (ReadMem(a8) - 1) And &HFF : WriteMem(a8, v8) : UpdateZN(v8) : Return 7
            Case &HE8 : X = (X + 1) And &HFF : UpdateZN(X) : Return 2
            Case &HCA : X = (X - 1) And &HFF : UpdateZN(X) : Return 2
            Case &HC8 : Y = (Y + 1) And &HFF : UpdateZN(Y) : Return 2
            Case &H88 : Y = (Y - 1) And &HFF : UpdateZN(Y) : Return 2

            ' --- Décalages ---
            Case &HA : A = DoASL(A) : Return 2
            Case &H6 : Dim s1 = AddrZp() : WriteMem(s1, DoASL(ReadMem(s1))) : Return 6
            Case &H16 : Dim s2 = AddrZpX() : WriteMem(s2, DoASL(ReadMem(s2))) : Return 6
            Case &HE : Dim s3 = AddrAbs() : WriteMem(s3, DoASL(ReadMem(s3))) : Return 7
            Case &H1E : Dim s4 = AddrAbsX() : WriteMem(s4, DoASL(ReadMem(s4))) : Return 7
            Case &H4A : A = DoLSR(A) : Return 2
            Case &H46 : Dim s5 = AddrZp() : WriteMem(s5, DoLSR(ReadMem(s5))) : Return 6
            Case &H56 : Dim s6 = AddrZpX() : WriteMem(s6, DoLSR(ReadMem(s6))) : Return 6
            Case &H4E : Dim s7 = AddrAbs() : WriteMem(s7, DoLSR(ReadMem(s7))) : Return 7
            Case &H5E : Dim s8 = AddrAbsX() : WriteMem(s8, DoLSR(ReadMem(s8))) : Return 7
            Case &H2A : A = DoROL(A) : Return 2
            Case &H26 : Dim s9 = AddrZp() : WriteMem(s9, DoROL(ReadMem(s9))) : Return 6
            Case &H36 : Dim sa = AddrZpX() : WriteMem(sa, DoROL(ReadMem(sa))) : Return 6
            Case &H2E : Dim sb = AddrAbs() : WriteMem(sb, DoROL(ReadMem(sb))) : Return 7
            Case &H3E : Dim sc = AddrAbsX() : WriteMem(sc, DoROL(ReadMem(sc))) : Return 7
            Case &H6A : A = DoROR(A) : Return 2
            Case &H66 : Dim sd = AddrZp() : WriteMem(sd, DoROR(ReadMem(sd))) : Return 6
            Case &H76 : Dim se = AddrZpX() : WriteMem(se, DoROR(ReadMem(se))) : Return 6
            Case &H6E : Dim sf = AddrAbs() : WriteMem(sf, DoROR(ReadMem(sf))) : Return 7
            Case &H7E : Dim sg = AddrAbsX() : WriteMem(sg, DoROR(ReadMem(sg))) : Return 7

            ' --- BIT / TSB / TRB / TST ---
            Case &H24 : DoBIT(ReadMem(AddrZp())) : Return 4
            Case &H2C : DoBIT(ReadMem(AddrAbs())) : Return 5
            Case &H34 : DoBIT(ReadMem(AddrZpX())) : Return 4
            Case &H3C : DoBIT(ReadMem(AddrAbsX())) : Return 5
            Case &H89 : SetFlag(FLAG_Z, (A And Fetch()) = 0) : Return 2
            Case &H4 : Dim t1 = AddrZp() : Dim tv1 = ReadMem(t1) : SetFlag(FLAG_Z, (A And tv1) = 0) : WriteMem(t1, tv1 Or A) : Return 6
            Case &HC : Dim t2 = AddrAbs() : Dim tv2 = ReadMem(t2) : SetFlag(FLAG_Z, (A And tv2) = 0) : WriteMem(t2, tv2 Or A) : Return 7
            Case &H14 : Dim t3 = AddrZp() : Dim tv3 = ReadMem(t3) : SetFlag(FLAG_Z, (A And tv3) = 0) : WriteMem(t3, tv3 And Not A) : Return 6
            Case &H1C : Dim t4 = AddrAbs() : Dim tv4 = ReadMem(t4) : SetFlag(FLAG_Z, (A And tv4) = 0) : WriteMem(t4, tv4 And Not A) : Return 7
            Case &H83 : Dim ti1 = Fetch() : Dim tm1 = ReadMem(AddrZp()) : TstFlags(ti1, tm1) : Return 7
            Case &H93 : Dim ti2 = Fetch() : Dim tm2 = ReadMem(AddrAbs()) : TstFlags(ti2, tm2) : Return 8
            Case &HA3 : Dim ti3 = Fetch() : Dim tm3 = ReadMem(AddrZpX()) : TstFlags(ti3, tm3) : Return 7
            Case &HB3 : Dim ti4 = Fetch() : Dim tm4 = ReadMem(AddrAbsX()) : TstFlags(ti4, tm4) : Return 8

            ' --- Branches ---
            Case &H10 : Branch((P And FLAG_N) = 0) : Return 3
            Case &H30 : Branch((P And FLAG_N) <> 0) : Return 3
            Case &H50 : Branch((P And FLAG_V) = 0) : Return 3
            Case &H70 : Branch((P And FLAG_V) <> 0) : Return 3
            Case &H90 : Branch((P And FLAG_C) = 0) : Return 3
            Case &HB0 : Branch((P And FLAG_C) <> 0) : Return 3
            Case &HD0 : Branch((P And FLAG_Z) = 0) : Return 3
            Case &HF0 : Branch((P And FLAG_Z) <> 0) : Return 3
            Case &H80 : Branch(True) : Return 4

            ' --- BBRi / BBSi ---
            Case &HF, &H1F, &H2F, &H3F, &H4F, &H5F, &H6F, &H7F
                Dim bitR = (op >> 4) And 7
                Dim zv1 = ReadMem(AddrZp())
                Branch((zv1 And (1 << bitR)) = 0)
                Return 8
            Case &H8F, &H9F, &HAF, &HBF, &HCF, &HDF, &HEF, &HFF
                Dim bitS = (op >> 4) And 7
                Dim zv2 = ReadMem(AddrZp())
                Branch((zv2 And (1 << bitS)) <> 0)
                Return 8

            ' --- RMBi / SMBi ---
            Case &H7, &H17, &H27, &H37, &H47, &H57, &H67, &H77
                Dim bitC = (op >> 4) And 7
                Dim ra = AddrZp()
                WriteMem(ra, ReadMem(ra) And Not (1 << bitC))
                Return 7
            Case &H87, &H97, &HA7, &HB7, &HC7, &HD7, &HE7, &HF7
                Dim bitD = (op >> 4) And 7
                Dim sa2 = AddrZp()
                WriteMem(sa2, ReadMem(sa2) Or (1 << bitD))
                Return 7

            ' --- Sauts ---
            Case &H4C : PC = FetchWord() : Return 4
            Case &H6C : Dim ja = FetchWord() : PC = ReadMem(ja) Or (ReadMem((ja + 1) And &HFFFF) << 8) : Return 7
            Case &H7C : Dim jb = (FetchWord() + X) And &HFFFF : PC = ReadMem(jb) Or (ReadMem((jb + 1) And &HFFFF) << 8) : Return 7
            Case &H20 : Dim ta = FetchWord() : Dim ret = (PC - 1) And &HFFFF : PushByte(ret >> 8) : PushByte(ret And &HFF) : PC = ta : Return 7
            Case &H60 : Dim lo6 = PopByte() : Dim hi6 = PopByte() : PC = ((lo6 Or (hi6 << 8)) + 1) And &HFFFF : Return 7
            Case &H40 : P = PopByte() : Dim lo4 = PopByte() : Dim hi4 = PopByte() : PC = lo4 Or (hi4 << 8) : Return 7
            Case &H44 : Dim rel8 = Fetch() : If rel8 >= &H80 Then rel8 -= 256
                Dim retB = (PC - 1) And &HFFFF
                PushByte(retB >> 8) : PushByte(retB And &HFF)
                PC = (PC + rel8) And &HFFFF
                Return 8

            ' --- Pile / transferts ---
            Case &H48 : PushByte(A) : Return 3
            Case &H68 : A = PopByte() : UpdateZN(A) : Return 4
            Case &H8 : PushByte(P Or FLAG_B) : Return 3
            Case &H28 : P = PopByte() : Return 4
            Case &HDA : PushByte(X) : Return 3
            Case &HFA : X = PopByte() : UpdateZN(X) : Return 4
            Case &H5A : PushByte(Y) : Return 3
            Case &H7A : Y = PopByte() : UpdateZN(Y) : Return 4
            Case &HAA : X = A : UpdateZN(X) : Return 2
            Case &H8A : A = X : UpdateZN(A) : Return 2
            Case &HA8 : Y = A : UpdateZN(Y) : Return 2
            Case &H98 : A = Y : UpdateZN(A) : Return 2
            Case &H9A : S = X : Return 2
            Case &HBA : X = S : UpdateZN(X) : Return 2

            ' --- Flags ---
            Case &H18 : SetFlag(FLAG_C, False) : Return 2
            Case &H38 : SetFlag(FLAG_C, True) : Return 2
            Case &H58 : SetFlag(FLAG_I, False) : Return 2
            Case &H78 : SetFlag(FLAG_I, True) : Return 2
            Case &HB8 : SetFlag(FLAG_V, False) : Return 2
            Case &HD8 : SetFlag(FLAG_D, False) : Return 2
            Case &HF8 : SetFlag(FLAG_D, True) : Return 2

            ' --- Spécifiques HuC6280 ---
            Case &H2 : Dim tx = X : X = Y : Y = tx : Return 3               ' SXY
            Case &H22 : Dim ta2 = A : A = X : X = ta2 : Return 3            ' SAX
            Case &H42 : Dim ta3 = A : A = Y : Y = ta3 : Return 3            ' SAY
            Case &H62 : A = 0 : Return 2                                     ' CLA
            Case &H82 : X = 0 : Return 2                                     ' CLX
            Case &HC2 : Y = 0 : Return 2                                     ' CLY
            Case &H54 : FastMode = False : Return 3                          ' CSL
            Case &HD4 : FastMode = True : Return 3                           ' CSH
            Case &HF4 : tFlagPending = True : P = P Or FLAG_T : Return 2     ' SET
            Case &H3 : mpu.WriteStoreImmediate(0, Fetch()) : Return 5        ' ST0
            Case &H13 : mpu.WriteStoreImmediate(2, Fetch()) : Return 5       ' ST1
            Case &H23 : mpu.WriteStoreImmediate(3, Fetch()) : Return 5       ' ST2

            Case &H53  ' TAM #mask
                Dim maskTam = Fetch()
                For i = 0 To 7
                    If (maskTam And (1 << i)) <> 0 Then mpu.SetMPR(i, A)
                Next
                Return 5

            Case &H43  ' TMA #mask
                Dim maskTma = Fetch()
                For i = 0 To 7
                    If (maskTma And (1 << i)) <> 0 Then
                        A = mpu.GetMPR(i)
                        Exit For
                    End If
                Next
                UpdateZN(A)
                Return 4

            ' --- Transferts de blocs ---
            Case &H73 : Return BlockTransfer(0)  ' TII
            Case &HC3 : Return BlockTransfer(1)  ' TDD
            Case &HD3 : Return BlockTransfer(2)  ' TIN
            Case &HE3 : Return BlockTransfer(3)  ' TIA
            Case &HF3 : Return BlockTransfer(4)  ' TAI

            ' --- NOP ---
            Case &HEA : Return 2

            Case Else
                ' Opcode inconnu : NOP
                Return 2
        End Select
    End Function

    Private Sub TstFlags(imm As Integer, mem As Integer)
        SetFlag(FLAG_Z, (imm And mem) = 0)
        SetFlag(FLAG_N, (mem And &H80) <> 0)
        SetFlag(FLAG_V, (mem And &H40) <> 0)
    End Sub

    ''' <summary>Transferts de blocs TII/TDD/TIN/TIA/TAI</summary>
    Private Function BlockTransfer(mode As Integer) As Integer
        Dim src = FetchWord()
        Dim dst = FetchWord()
        Dim len = FetchWord()
        If len = 0 Then len = &H10000

        For i = 0 To len - 1
            Dim curSrc = src
            Dim curDst = dst
            Select Case mode
                Case 3 : curDst = (dst + (i And 1)) And &HFFFF   ' TIA : dest alterne dst/dst+1
                Case 4 : curSrc = (src + (i And 1)) And &HFFFF   ' TAI : source alterne src/src+1
            End Select
            Dim v = ReadMem(curSrc)
            WriteMem(curDst, v)
            Select Case mode
                Case 0 : src = (src + 1) And &HFFFF : dst = (dst + 1) And &HFFFF   ' TII
                Case 1 : src = (src - 1) And &HFFFF : dst = (dst - 1) And &HFFFF   ' TDD
                Case 2 : src = (src + 1) And &HFFFF                                 ' TIN
                Case 3 : src = (src + 1) And &HFFFF                                 ' TIA
                Case 4 : dst = (dst + 1) And &HFFFF                                 ' TAI
            End Select
        Next
        Return 17 + 6 * len
    End Function


    ''' <summary>Écrit l'état du CPU dans une sauvegarde.</summary>
    Public Sub SaveState(w As System.IO.BinaryWriter)
        w.Write(A) : w.Write(X) : w.Write(Y) : w.Write(S) : w.Write(PC) : w.Write(P)
        w.Write(CyclesThisFrame)
        w.Write(Halted) : w.Write(FastMode)
        w.Write(tFlagPending) : w.Write(tFlagActive)
    End Sub

    ''' <summary>Restaure l'état du CPU depuis une sauvegarde.</summary>
    Public Sub LoadState(r As System.IO.BinaryReader)
        A = r.ReadInt32() : X = r.ReadInt32() : Y = r.ReadInt32()
        S = r.ReadInt32() : PC = r.ReadInt32() : P = r.ReadInt32()
        CyclesThisFrame = r.ReadInt64()
        Halted = r.ReadBoolean() : FastMode = r.ReadBoolean()
        tFlagPending = r.ReadBoolean() : tFlagActive = r.ReadBoolean()
    End Sub

End Class
