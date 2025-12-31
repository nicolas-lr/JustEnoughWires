using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.Xbox360;

// REQUISITOS:
// 1. Instalar NuGet: Nefarius.ViGEm.Client
// 2. Instalar Driver no Windows: ViGEmBus Setup (v1.21.442+)

namespace XboxRemoteServer
{
    class Program
    {
        // --- CONFIGURAÇÕES DE PORTA ---
        private const int PORTA_RECEBE = 8080;         // Porta que o Celular manda dados
        private const int PORTA_ENVIA_ANDROID = 9090;  // Porta para devolver vibração

        // --- OBJETOS GLOBAIS ---
        private static UdpClient _udpServer;
        private static IPEndPoint _androidEndPoint; // Guarda o IP do celular
        private static Dictionary<int, Xbox360Button> _mapaBotoesAndroid;

        static void Main(string[] args)
        {
            Console.Title = "Servidor Xbox Remoto (Final)";
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("=== INICIANDO SERVIDOR ===");
            Console.ResetColor();

            // 1. Configura Dicionário (Android Key -> Xbox Button)
            ConfigurarMapaBotoes();

            // 2. Inicializa o Controle Virtual (ViGEm)
            ViGEmClient client;
            IXbox360Controller controller;

            try
            {
                Console.Write("Carregando Driver ViGEmBus... ");
                client = new ViGEmClient();
                controller = client.CreateXbox360Controller();
                
                // Evento de Vibração (Rumble)
                controller.FeedbackReceived += (sender, e) => EnviarVibracaoParaAndroid(e.LargeMotor, e.SmallMotor);
                
                controller.Connect();
                Console.WriteLine("SUCESSO! (Controle Virtual Criado)");
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\n[ERRO CRÍTICO] Falha no Driver: {ex.Message}");
                Console.WriteLine("Você instalou o ViGEmBus Driver no Windows?");
                Console.ResetColor();
                Console.ReadLine();
                return;
            }

            // 3. Inicializa a Rede UDP
            try
            {
                _udpServer = new UdpClient(PORTA_RECEBE);
                
                // Correção para bug de UDP no Windows (ConnectionReset)
                if (OperatingSystem.IsWindows())
                {
                    const int SIO_UDP_CONNRESET = -1744830452;
                    _udpServer.Client.IOControl((IOControlCode)SIO_UDP_CONNRESET, new byte[] { 0, 0, 0, 0 }, null);
                }

                ExibirIpLocal();
                Console.WriteLine($"\nPronto! Aguardando conexão do Android na porta {PORTA_RECEBE}...");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERRO REDE] {ex.Message}");
                return;
            }

            // 4. Loop Principal (Recebimento de Dados)
            IPEndPoint remoteEP = new IPEndPoint(IPAddress.Any, PORTA_RECEBE);
            string ultimoIpConectado = "";

            while (true)
            {
                try
                {
                    // Bloqueia a thread até chegar dados
                    byte[] data = _udpServer.Receive(ref remoteEP);

                    // Atualiza destino da vibração
                    _androidEndPoint = new IPEndPoint(remoteEP.Address, PORTA_ENVIA_ANDROID);

                    // Log de nova conexão apenas uma vez
                    if (ultimoIpConectado != remoteEP.Address.ToString())
                    {
                        ultimoIpConectado = remoteEP.Address.ToString();
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine($"[CONEXÃO] Novo Cliente: {ultimoIpConectado}");
                        Console.ResetColor();
                    }

                    // Processa o pacote recebido
                    ProcessarEntrada(controller, data);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Erro Loop] {ex.Message}");
                }
            }
        }

        // --- ROTEADOR DE PACOTES ---
        static void ProcessarEntrada(IXbox360Controller controller, byte[] d)
        {
            if (d == null || d.Length == 0) return;

            byte header = d[0];

            switch (header)
            {
                // --- NOVO: HANDSHAKE / PING ---
                case 0xF0:
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"   [PING] Handshake recebido! Conexão OK.");
                    Console.ResetColor();
                    break;

                // --- BOTÃO GUIDE / HOME ---
                case 0x07:
                    if (d.Length >= 5)
                    {
                        bool guidePressed = d[4] != 0;
                        controller.SetButtonState(Xbox360Button.Guide, guidePressed);
                    }
                    break;

                // --- PROTOCOLO NATIVO (Android) ---
                case 0xA1: // EIXOS
                    if (d.Length >= 9) ProcessarTodosEixos(controller, d);
                    break;

                case 0xB1: // BOTÃO DOWN
                    if (d.Length >= 2) ProcessarBotao(controller, true, d[1]);
                    break;

                case 0xB0: // BOTÃO UP
                    if (d.Length >= 2) ProcessarBotao(controller, false, d[1]);
                    break;

                // --- PROTOCOLO RAW (Padrão) ---
                default:
                    ProcessarDadosRaw(controller, d);
                    break;
            }
        }

        // --- LÓGICA 1: EIXOS, GATILHOS E D-PAD ---
        static void ProcessarTodosEixos(IXbox360Controller controller, byte[] d)
        {
            // Estrutura: [A1, LX, LY, RX, RY, LT, RT, HatX, HatY]

            // --- 1. ANALÓGICOS COM DEADZONE ---
            controller.SetAxisValue(Xbox360Axis.LeftThumbX,  AplicarDeadzone(d[1], false)); // X Normal
            controller.SetAxisValue(Xbox360Axis.LeftThumbY,  AplicarDeadzone(d[2], true));  // Y Invertido
            
            controller.SetAxisValue(Xbox360Axis.RightThumbX, AplicarDeadzone(d[3], false)); // X Normal
            controller.SetAxisValue(Xbox360Axis.RightThumbY, AplicarDeadzone(d[4], true));  // Y Invertido

            // --- 2. GATILHOS (L2/R2) ---
            controller.SetSliderValue(Xbox360Slider.LeftTrigger, d[5]);
            controller.SetSliderValue(Xbox360Slider.RightTrigger, d[6]);

            // --- 3. D-PAD (HAT SWITCH) ---
            byte hatX = d[7];
            byte hatY = d[8];

            // Esquerda/Direita
            if (hatX < 64) {
                controller.SetButtonState(Xbox360Button.Left, true);
                controller.SetButtonState(Xbox360Button.Right, false);
            } else if (hatX > 192) {
                controller.SetButtonState(Xbox360Button.Left, false);
                controller.SetButtonState(Xbox360Button.Right, true);
            } else {
                controller.SetButtonState(Xbox360Button.Left, false);
                controller.SetButtonState(Xbox360Button.Right, false);
            }

            // Cima/Baixo
            if (hatY < 64) {
                controller.SetButtonState(Xbox360Button.Up, true);
                controller.SetButtonState(Xbox360Button.Down, false);
            } else if (hatY > 192) {
                controller.SetButtonState(Xbox360Button.Up, false);
                controller.SetButtonState(Xbox360Button.Down, true);
            } else {
                controller.SetButtonState(Xbox360Button.Up, false);
                controller.SetButtonState(Xbox360Button.Down, false);
            }
        }

        // --- FUNÇÃO AUXILIAR MATEMÁTICA ---
        static short AplicarDeadzone(byte rawValue, bool inverter)
        {
            const int DEADZONE = 10; 
            int centro = 128;

            if (Math.Abs(rawValue - centro) < DEADZONE) return 0;

            int resultado;
            if (inverter) resultado = (centro - rawValue) * 256;
            else resultado = (rawValue - centro) * 256;

            if (resultado > 32767) return 32767;
            if (resultado < -32768) return -32768;

            return (short)resultado;
        }

        // --- LÓGICA 2: BOTÕES DIGITAIS (Restaurado!) ---
        static void ProcessarBotao(IXbox360Controller controller, bool pressionado, byte keyCode)
        {
            if (_mapaBotoesAndroid.ContainsKey(keyCode))
            {
                controller.SetButtonState(_mapaBotoesAndroid[keyCode], pressionado);
            }
        }

        // --- LÓGICA 3: DADOS RAW (Compatibilidade USB) ---
        static void ProcessarDadosRaw(IXbox360Controller controller, byte[] d)
        {
            if (d.Length < 14) return; // Pacote muito pequeno para ser USB Xbox

            try
            {
                controller.SetButtonState(Xbox360Button.Guide, (d[4] & 0x01) != 0);

                int offset = (d[0] != 0x20 && d.Length > 15) ? 1 : 0;

                controller.SetButtonState(Xbox360Button.Start, (d[4 + offset] & 0x04) != 0);
                controller.SetButtonState(Xbox360Button.Back,  (d[4 + offset] & 0x08) != 0);
                controller.SetButtonState(Xbox360Button.A,     (d[4 + offset] & 0x10) != 0);
                controller.SetButtonState(Xbox360Button.B,     (d[4 + offset] & 0x20) != 0);
                controller.SetButtonState(Xbox360Button.X,     (d[4 + offset] & 0x40) != 0);
                controller.SetButtonState(Xbox360Button.Y,     (d[4 + offset] & 0x80) != 0);
                
                controller.SetButtonState(Xbox360Button.Up,    (d[5 + offset] & 0x01) != 0);
                controller.SetButtonState(Xbox360Button.Down,  (d[5 + offset] & 0x02) != 0);
                controller.SetButtonState(Xbox360Button.Left,  (d[5 + offset] & 0x04) != 0);
                controller.SetButtonState(Xbox360Button.Right, (d[5 + offset] & 0x08) != 0);

                controller.SetButtonState(Xbox360Button.LeftShoulder,  (d[5 + offset] & 0x10) != 0);
                controller.SetButtonState(Xbox360Button.RightShoulder, (d[5 + offset] & 0x20) != 0);
                controller.SetButtonState(Xbox360Button.LeftThumb,     (d[5 + offset] & 0x40) != 0);
                controller.SetButtonState(Xbox360Button.RightThumb,    (d[5 + offset] & 0x80) != 0);

                if (d.Length >= (18 + offset))
                {
                    int rawLT = d[6 + offset] | (d[7 + offset] << 8);
                    controller.SetSliderValue(Xbox360Slider.LeftTrigger, (byte)(rawLT >> 2));

                    int rawRT = d[8 + offset] | (d[9 + offset] << 8);
                    controller.SetSliderValue(Xbox360Slider.RightTrigger, (byte)(rawRT >> 2));

                    controller.SetAxisValue(Xbox360Axis.LeftThumbX,  BitConverter.ToInt16(d, 10 + offset));
                    controller.SetAxisValue(Xbox360Axis.LeftThumbY,  BitConverter.ToInt16(d, 12 + offset));
                    controller.SetAxisValue(Xbox360Axis.RightThumbX, BitConverter.ToInt16(d, 14 + offset));
                    controller.SetAxisValue(Xbox360Axis.RightThumbY, BitConverter.ToInt16(d, 16 + offset));
                }
            }
            catch { /* Ignora pacote corrompido */ }
        }

        // --- AUXILIARES ---
        static void EnviarVibracaoParaAndroid(byte motorForte, byte motorFraco)
        {
            if (_androidEndPoint == null) return;
            byte[] pacote = { 0xFE, motorForte, motorFraco };
            try { _udpServer.Send(pacote, pacote.Length, _androidEndPoint); } catch { }
        }

        static void ExibirIpLocal()
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());
            foreach (var ip in host.AddressList)
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork)
                    Console.WriteLine($"Endereço IP Local: {ip}");
            }
        }

        static void ConfigurarMapaBotoes()
        {
            _mapaBotoesAndroid = new Dictionary<int, Xbox360Button>
            {
                { 96, Xbox360Button.A },
                { 97, Xbox360Button.B },
                { 99, Xbox360Button.X },
                { 100, Xbox360Button.Y },
                { 102, Xbox360Button.LeftShoulder },
                { 103, Xbox360Button.RightShoulder },
                { 108, Xbox360Button.Start },
                { 109, Xbox360Button.Back },
                { 106, Xbox360Button.LeftThumb },
                { 107, Xbox360Button.RightThumb }
            };
        }
    }
}