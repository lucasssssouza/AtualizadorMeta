using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Diagnostics;
using System.IO.Compression;
using System.Threading.Tasks;

class Program
{
    // 1) Raw URL do versao.json no GitHub
    const string jsonUrl =
      "https://raw.githubusercontent.com/lucasssssouza/atualizadorMeta/main/versao.json";
    // 2) Template da URL do ZIP no Release (note o "v" + tag)
    const string zipUrlTemplate =
      "https://github.com/lucasssssouza/atualizador/releases/download/v{0}/{1}";

    const string destinoBase = @"C:\Ganso\Meta\";
    const string nomeExePrincipal = "Criar Meta";

    static async Task Main()
    {
        Console.WriteLine("Verificando nova versão...");

        var info = await ObterVersaoRemota();
        if (info == null) return;

        string local = ObterVersaoLocal();
        Console.WriteLine($"Versão local:  {local}");
        Console.WriteLine($"Versão server: {info.versao}");

        if (new Version(info.versao) <= new Version(local))
        {
            Console.WriteLine("Nenhuma atualização necessária.");
            PromptExit();
            return;
        }

        Console.WriteLine("Atualizando para v" + info.versao + "...");

        Console.WriteLine("Encerrando instâncias de " + nomeExePrincipal + "...");
        EncerrarProgramaPrincipal();

        string tempDir = Path.Combine(destinoBase, "TEMP_UPDATE");
        Directory.CreateDirectory(tempDir);

        string zipName = info.arquivoZip;
        string zipUrl = string.Format(zipUrlTemplate, info.versao, zipName);
        string zipTemp = Path.Combine(tempDir, zipName);

        Console.WriteLine($"Baixando {zipName}\n");
        await BaixarZipHttp(zipUrl, zipTemp);

        Console.WriteLine("Removendo arquivos antigos ...\n");
        LimparArquivosDestino();

        Console.WriteLine("Extraindo atualização ...\n");
        using (var zip = ZipFile.OpenRead(zipTemp))
        {
            foreach (var entry in zip.Entries)
            {
                if (Path.GetFileNameWithoutExtension(entry.Name)
                        .Equals("Atualizador", StringComparison.OrdinalIgnoreCase))
                    continue;

                string destPath = Path.Combine(destinoBase, entry.FullName);
                Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
                entry.ExtractToFile(destPath, overwrite: true);
            }
        }

        Console.WriteLine("Limpando arquivos temporários...");
        File.Delete(zipTemp);
        Directory.Delete(tempDir, true);

        SalvarVersaoLocal(info.versao);
        Console.WriteLine("\n\n================== Atualização concluída com sucesso. ==================\n\n");

        // ❽ Reinicia o app principal e encerra este Atualizador
        string exePath = Path.Combine(destinoBase, nomeExePrincipal + ".exe");
        if (File.Exists(exePath))
        {
            Console.WriteLine("\n\n================== Iniciando " + nomeExePrincipal + " ==================\n\n");
            Process.Start(exePath);
            return;  // sai de Main, fechando o Atualizador automaticamente
        }
        else
        {
            Console.WriteLine("Executável principal não encontrado: " + exePath);
            PromptExit();
        }
    }

    static void PromptExit()
    {
        Console.WriteLine("\nPressione ENTER para sair...");
        Console.ReadLine();
    }

    static void EncerrarProgramaPrincipal()
    {
        foreach (var proc in Process.GetProcessesByName(nomeExePrincipal))
        {
            try
            {
                proc.Kill(true);
                proc.WaitForExit();
                Console.WriteLine($"Encerrado: {proc.ProcessName} (PID {proc.Id})");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Falha ao encerrar {proc.ProcessName}: {ex.Message}");
            }
        }
    }

    static async Task<InfoVersao?> ObterVersaoRemota()
    {
        using var cli = new HttpClient();
        try
        {
            var json = await cli.GetStringAsync(jsonUrl);
            return JsonSerializer.Deserialize<InfoVersao>(json);
        }
        catch (Exception ex)
        {
            Console.WriteLine("Erro ao baixar versao.json: " + ex.Message);
            return null;
        }
    }

    static async Task BaixarZipHttp(string url, string destino)
    {
        using var cli = new HttpClient();
        using var resp = await cli.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        resp.EnsureSuccessStatusCode();

        long? totalBytes = resp.Content.Headers.ContentLength;
        using var src = await resp.Content.ReadAsStreamAsync();
        using var dst = new FileStream(destino, FileMode.Create, FileAccess.Write);

        var buffer = new byte[8192];
        long bytesReadTotal = 0;
        int read;
        Console.Write("Progresso: ");

        while ((read = await src.ReadAsync(buffer, 0, buffer.Length)) > 0)
        {
            await dst.WriteAsync(buffer, 0, read);
            bytesReadTotal += read;

            if (totalBytes.HasValue)
            {
                int pct = (int)(bytesReadTotal * 100 / totalBytes.Value);
                Console.Write($"\rProgresso: {pct,3}% ({bytesReadTotal / 1024,6}KB/{totalBytes.Value / 1024,6}KB)");
            }
            else
            {
                Console.Write($"\rProgresso: {bytesReadTotal / 1024,6} KB");
            }
        }

        Console.WriteLine(); // termina a linha
        Console.WriteLine("Download concluído.");
    }

    static void LimparArquivosDestino()
    {
        if (!Directory.Exists(destinoBase)) return;

        foreach (var caminho in Directory.GetFiles(destinoBase))
        {
            string nomeArquivo = Path.GetFileName(caminho);
            string baseName = Path.GetFileNameWithoutExtension(nomeArquivo);

            if (baseName.Equals("Atualizador", StringComparison.OrdinalIgnoreCase))
                continue;

            try
            {
                File.Delete(caminho);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erro ao excluir {nomeArquivo}: {ex.Message}");
            }
        }
    }

    static string ObterVersaoLocal()
    {
        var p = Path.Combine(destinoBase, "versao.txt");
        return File.Exists(p) ? File.ReadAllText(p).Trim() : "0.0.0";
    }

    static void SalvarVersaoLocal(string v)
        => File.WriteAllText(Path.Combine(destinoBase, "versao.txt"), v);

    static bool VersaoNova(string local, string server)
        => new Version(server) > new Version(local);
}

class InfoVersao
{
    public string versao { get; set; }
    public string arquivoZip { get; set; }
}
