# Just Enough Wires - Xbox Controller Server

Um servidor leve e de alto desempenho escrito em C# que transforma o seu dispositivo Android em uma **ponte (relay) sem fio** entre um controle físico e o seu PC.

Ele resolve o problema de distância ou falta de adaptadores nativos: você conecta o seu controle via cabo OTG ou Bluetooth no celular, e o servidor recebe esses dados brutos (Raw) via rede local (UDP), traduzindo-os para inputs virtuais graças ao ViGEmBus. O resultado é uma jogatina com zero atraso perceptível e compatibilidade com 100% dos jogos de PC.

## Como a Arquitetura Funciona

1. **Controle Físico -> Smartphone:** O controle é conectado ao smartphone (via USB OTG ou Bluetooth).
2. **O App Android (Ponte):** Lê os pacotes Hexadecimais puros que saem do hardware e os envia via Wi-Fi usando o protocolo UDP.
3. **Servidor C# (Este projeto):** Escuta a porta `8080` no PC, decodifica os pacotes originais e aciona o driver ViGEm para criar um controle Xbox 360 virtual no Windows.

## ⚠️ Pré-requisitos Obrigatórios ⚠️

Para rodar este servidor no seu computador, você precisará de:

* **Sistema Operacional:** Windows 10 ou 11.
* **Driver ViGEmBus:** O servidor precisa deste driver instalado para criar o controle virtual que os jogos vão reconhecer. 
  * [Baixe e instale o ViGEmBus aqui](https://github.com/nefarius/ViGEmBus/releases/latest)
* **Rede:** O celular e o PC precisam estar na **mesma rede Wi-Fi/LAN**.

## Como Executar

### Opção 1: Executável Pronto (Recomendado para Usuários)
Se você quer apenas jogar, não precisa instalar ferramentas de programação.
1. Vá até a aba **[Releases](../../releases)** aqui no lado direito do repositório.
2. Baixe o arquivo `.exe` (ou `.zip`) da versão mais recente.
3. Certifique-se de que o **ViGEmBus** está instalado.
4. Dê um duplo clique no executável do servidor.
5. *Nota: Na primeira vez que você abrir, o Firewall do Windows pode perguntar se você permite que o aplicativo use a rede. Clique em **Permitir** para que ele possa receber os dados do celular.*

### Opção 2: Via Código-Fonte

**Ferramentas necessárias para compilar:**
* [.NET 10 SDK](https://dotnet.microsoft.com/download)
* [ViGEmBus](https://github.com/nefarius/ViGEmBus/releases/latest) 


1. Clone este repositório:
   ```bash
   git clone https://github.com/nicolas-lr/JustEnoughWires.git
   
2. Acesse a pasta do projeto que acabou de ser criada:
   ```bash
   cd JustEnoughWires

3. Restaure as dependências do NuGet (Nefarius.ViGEm.Client):
   ```bash
   dotnet restore
4. Compile e execute o servidor:
    ```bash
    dotnet run
