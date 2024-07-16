using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

using iText.Kernel.Pdf.Canvas.Parser.Listener;
using iText.Kernel.Pdf.Canvas.Parser;

using iText.Kernel.Pdf;
using System.Text.Json;
using Azure.AI.OpenAI;
using Azure;

public class ParagraphVector
{
    public string Content { get; set; }
    public List<float> Vectors { get; set; }
}

public class PineconeVector
{
    public string Id { get; set; }
    public List<float> Values { get; set; }
    public Dictionary<string, string> Metadata { get; set; }
}

class Program
{
    static async Task Main(string[] args)
    {
        string pdfPath = "hotel-valle-del-volcan.pdf"; // Path to the PDF file
        string text = ExtractTextFromPdf(pdfPath);
        List<string> paragraphs = SplitIntoParagraphs(text);
        List<ParagraphVector> paragraphVectors = new List<ParagraphVector>();

        foreach (var paragraph in paragraphs)
        {
            var vectors = await GetEmbeddings(paragraph);
            paragraphVectors.Add(new ParagraphVector { Content = paragraph, Vectors = vectors.ToList() });
        }

        await UploadVectorsToPinecone(paragraphVectors);
    }

    static string ExtractTextFromPdf(string path)
    {
        StringBuilder text = new StringBuilder();

        using (PdfReader pdfReader = new PdfReader(path))
        using (PdfDocument pdfDocument = new PdfDocument(pdfReader))
        {
            for (int i = 1; i <= pdfDocument.GetNumberOfPages(); i++)
            {
                var page = pdfDocument.GetPage(i);
                var strategy = new SimpleTextExtractionStrategy();
                string pageText = PdfTextExtractor.GetTextFromPage(page, strategy);
                text.Append(pageText);
            }
        }

        return text.ToString();
    }

    static List<string> SplitIntoParagraphs(string text)
    {
        return new List<string>(text.Split(new[] { "\r\n\r\n", "\n \n" }, StringSplitOptions.None).Where(x => x.Trim() != string.Empty));
    }

    static async Task<IEnumerable<float>> GetEmbeddings(string text)
    {
        OpenAIClient client = new OpenAIClient(
  new Uri(""),
  new AzureKeyCredential(""));


        var userQuestionEmbedding = await client.GetEmbeddingsAsync(new EmbeddingsOptions("embedding", [text]));

        return userQuestionEmbedding.Value.Data[0].Embedding.ToArray();   
    }

    static async Task UploadVectorsToPinecone(List<ParagraphVector> paragraphVectors)
    {
        using (HttpClient client = new HttpClient())
        {
            string apiKey = "YOUR_API_KEY";
            string endpoint = "https://YOUR_INDEX_ENDPOINT/vectors/upsert";
            client.DefaultRequestHeaders.Add("Api-Key", apiKey);

            var vectors = new List<PineconeVector>();
            int id = 1;

            foreach (var paragraphVector in paragraphVectors)
            {
                vectors.Add(new PineconeVector
                {
                    Id = "vec" + id++,
                    Values = paragraphVector.Vectors,
                    Metadata = new Dictionary<string, string> { { "content", paragraphVector.Content } }
                });
            }

            var payload = new { vectors = vectors, @namespace = "ns1" };

            string jsonPayload = JsonSerializer.Serialize(payload, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase   
            });

            var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
            HttpResponseMessage response = await client.PostAsync(endpoint, content);

            if (response.IsSuccessStatusCode)
            {
                Console.WriteLine("Vectors uploaded successfully.");
            }
            else
            {
                Console.WriteLine("Error uploading vectors: " + response.ReasonPhrase);
            }
        }
    }
}

