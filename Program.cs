using ElevenLabs;
using English_bot;
using OpenAI;
using OpenAI.Managers;
using OpenAI.ObjectModels;
using OpenAI.ObjectModels.RequestModels;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using File = System.IO.File;
using MemoryStream = System.IO.MemoryStream;
using Voice = ElevenLabs.Voices.Voice;

public class Program
{
    private static OpenAIService? _openAiService;
    private static List<ChatMessage> _messages = new ();
    private static ElevenLabsClient? _elevenLabsClient;
    private static Voice? _voice;

    public static async Task Main()
    {
        _elevenLabsClient = new ElevenLabsClient(Resources.ELEVENLABS_API_KEY);
        _voice = (await _elevenLabsClient.VoicesEndpoint.GetAllVoicesAsync()).FirstOrDefault();
        
        InitializeOpenAi();
        
        var client = CreateBot();
        StartReceiving(client);

        Console.ReadKey();
    }

    private static TelegramBotClient CreateBot()
    {
        var client = new TelegramBotClient(Resources.OPENAI_KEY);

        client.SetMyCommandsAsync(new List<BotCommand>
        {
            new()
            {
                Command = CustomBotCommands.START,
                Description = "Запустить бота"
            }
        });
        return client;
    }

    private static void StartReceiving(TelegramBotClient client)
    {
        var cancellationTokenSource = new CancellationTokenSource();
        var cancellationToken = new CancellationToken();

        var receiverOptions = new ReceiverOptions
        {
            AllowedUpdates = new UpdateType[] { UpdateType.Message }
        };

        client.StartReceiving(
            HandleUpdateAsync,
            HandleError,
            receiverOptions,
            cancellationToken
        );
    }

    private static void InitializeOpenAi()
    {
        _openAiService = new OpenAIService(new OpenAiOptions()
        {
            ApiKey = Resources.BOT_TOKEN
        });
        
        _messages.Add(ChatMessage.FromSystem(Resources.INITIAL_PROMT));
    }

    private static Task HandleError(ITelegramBotClient arg1, Exception arg2, CancellationToken arg3)
    {
        return Task.CompletedTask;
    }

    private static async Task HandleUpdateAsync(ITelegramBotClient client, Update update, CancellationToken cancellationToken)
    {
        var voiceFileId = update.Message?.Voice?.FileId;
        
        if (voiceFileId != null)
        {
            var stream = await DownloadVoice(client, cancellationToken, voiceFileId);
            var transcription = await GetTranscription(cancellationToken, stream);
            var aiResponse = await GetResponseFromAiAndSendToUser(client, update, cancellationToken, transcription);
            
            Console.WriteLine(aiResponse);
            return;
        }

        var userMessage = update.Message?.Text;
        
        if (userMessage != null)
        {
            var aiResponse = await GetResponseFromAiAndSendToUser(client, update, cancellationToken, userMessage);
            Console.WriteLine(aiResponse);
            return;
        }
    }

    private static async Task<string> GetResponseFromAiAndSendToUser(ITelegramBotClient client, Update update,
        CancellationToken cancellationToken, string userMessage)
    {
        _messages.Add(ChatMessage.FromUser(userMessage));

        var chatCompletion = _openAiService?.ChatCompletion;
        var result = await chatCompletion?.CreateCompletion(new ChatCompletionCreateRequest
        {
            Messages = _messages,
            Model = Models.Gpt_3_5_Turbo,
            MaxTokens = 1024
        }, cancellationToken: cancellationToken)!;

        var aiResponse = result.Choices.First().Message.Content;
        await SendResponse(client, update, aiResponse, cancellationToken);

        _messages.Add(ChatMessage.FromAssistant(aiResponse));
        return aiResponse;
    }

    private static async Task<string?> GetTranscription(CancellationToken cancellationToken, MemoryStream stream)
    {
        var transcriptionResponse = await _openAiService?.Audio.CreateTranscription(new AudioCreateTranscriptionRequest()
        {
            FileName = "test.ogg",
            File = stream.ToArray(),
            Model = Models.WhisperV1
        }, cancellationToken)!;

        return transcriptionResponse?.Text;
    }

    private static async Task<MemoryStream> DownloadVoice(ITelegramBotClient client, CancellationToken cancellationToken,
        string voiceFileId)
    {
        var stream = new MemoryStream();
        await client.GetInfoAndDownloadFileAsync(voiceFileId, stream, cancellationToken);
        stream.Close();
        return stream;
    }

    private static async Task SendResponse(ITelegramBotClient client, Update update, string message, CancellationToken cancellationToken)
    {
        var chatId = update.Message?.Chat.Id;

        if (chatId != null)
        {
            var responseVoice = await TextToSpeech(message);
            
            using Stream stream = new MemoryStream(responseVoice);
            
            await client.SendAudioAsync(
                chatId: chatId,
                audio: InputFile.FromStream(stream),
                cancellationToken: cancellationToken);
            
            //await client.SendTextMessageAsync(chatId, text: message, cancellationToken: cancellationToken);
        }
    }

    private static async Task<byte[]> TextToSpeech(string text)
    {
        var voiceClip = await _elevenLabsClient.TextToSpeechEndpoint.TextToSpeechAsync(text, _voice);
        return voiceClip.ClipData.ToArray();
    }
    
    /*private static async Task TextToSpeech()
    {
        _elevenLabsClient = new ElevenLabsClient(Resources.ELEVENLABS_API_KEY);
        var text = "How are you?";
        var voice = (await _elevenLabsClient.VoicesEndpoint.GetAllVoicesAsync()).FirstOrDefault();
        var voiceClip = await _elevenLabsClient.TextToSpeechEndpoint.TextToSpeechAsync(text, voice);
        await File.WriteAllBytesAsync($"{voiceClip.Id}.mp3", voiceClip.ClipData.ToArray());
    }*/
}

