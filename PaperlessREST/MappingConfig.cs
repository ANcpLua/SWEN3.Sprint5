using JetBrains.Annotations;
using Mapster;
using PaperlessREST.DAL;

namespace PaperlessREST;

[UsedImplicitly]
public class MappingConfig : IRegister
{
    [Generate]
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<DocumentEntity, Document>().MapToConstructor(true);

        config.NewConfig<Document, DocumentEntity>();

        config.NewConfig<Document, DocumentDto>().Map(dest => dest.Status, src => src.Status.ToString());

        config.NewConfig<Document, CreateDocumentResponse>().Map(dest => dest.Status, src => src.Status.ToString());
    }
}