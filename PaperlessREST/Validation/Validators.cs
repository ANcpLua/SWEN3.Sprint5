using System.ComponentModel.DataAnnotations;
using FluentValidation;
using JetBrains.Annotations;
using PaperlessREST.DAL;

namespace PaperlessREST.Validation;

public class UploadDocumentRequest : IValidatableObject
{
    [Required(ErrorMessage = "File is required")]
    public IFormFile File { get; set; } = null!;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!File.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            yield return new ValidationResult("Only PDF files are allowed", [nameof(File)]);

        if (File.Length > 50 * 1024 * 1024)
            yield return new ValidationResult("File size must not exceed 50MB", [nameof(File)]);
    }
}

[UsedImplicitly]
public class UploadDocumentBusinessValidator : AbstractValidator<UploadDocumentRequest>
{
    public UploadDocumentBusinessValidator(IDocumentRepository repository)
    {
        RuleFor(x => x.File.FileName).MustAsync(async (fileName, cancellation) =>
        {
            var exists = await repository.FileNameExistsAsync(fileName, cancellation);
            return !exists;
        }).WithMessage("A document with this filename already exists");
    }
}