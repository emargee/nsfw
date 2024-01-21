using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Nsfw.Commands;

public class NiSettings : CommandSettings
{
    [CommandOption("-c|--cdndat <FILE>")]
    [Description("Path to NI CDN Dat file.")]
    public string CdnDat { get; set; } = string.Empty;
    
    [CommandOption("-n|--nspdat <FILE>")]
    [Description("Path to NI NSP Dat file.")]
    public string NspDat { get; set; } = string.Empty;
    
    [CommandOption("-d|--dlcdat <FILE>")]
    [Description("Path to NI DLC NSP Dat file.")]
    public string DlcDat { get; set; } = string.Empty;
    
    [CommandOption("-i|--scandir <FILE>")]
    [Description("Path to NSP directory to scan.")]
    public string ScanDir { get; set; } = string.Empty;
    
    [CommandOption("-V|--show-all")]
    [Description("Show matching NSP file as well as missing NSP files.")]
    public bool ShowCorrect { get; set; }
    
    [CommandOption("-x|--correct-name")]
    [Description("Prompt to rename NSP to NI version.")]
    public bool CorrectName { get; set; }
    
    [CommandOption("-r |--reverse")]
    [Description("Reverse the scan - show files not in the dat.")]
    public bool Reverse { get; set; }
    
    [CommandOption("-l|--letter <LETTER>")]
    [Description("Filter by single letter.")]
    [DefaultValue(null)]
    public string? ByLetter { get; set; }
    
    [CommandOption("-s|--save-dat")]
    [Description("If set, saves a dat of missing to this directory.")]
    public string? SaveDatDirectory { get; set; }
    
    [CommandOption("--show-duplicates")]
    [Description("If set, shows duplicate NSP files found in DATs.")]
    public bool ShowDuplicates { get; set; }
    
    [CommandOption("-X|--exclude-dlc")]
    [Description("If set, excludes DLC NSP files from scan.")]
    public bool ExcludeDlc { get; set; }
    
    [CommandOption("-T|--show-tik-missing")]
    [Description("If set, shows CDN entries with missing tik files (unfixable).")]
    public bool ShowTikMissing { get; set; }
    
    [CommandOption("-M|--show-missing")]
    [Description("If set, list missing latest 20 NSP files in NI ID order.")]
    public bool ShowMissing { get; set; }
    
    public override ValidationResult Validate()
    {
        if (string.IsNullOrWhiteSpace(CdnDat))
        {
            return ValidationResult.Error("CDN Dat file is required.");
        }
        
        if (string.IsNullOrWhiteSpace(NspDat))
        {
            return ValidationResult.Error("NSP Dat file is required.");
        }
        
        if (string.IsNullOrWhiteSpace(ScanDir))
        {
            return ValidationResult.Error("Scan directory is required.");
        }
        
        if (string.IsNullOrWhiteSpace(DlcDat))
        {
            return ValidationResult.Error("DLC Dat file is required.");
        }
        
        if(!File.Exists(DlcDat))
        {
            return ValidationResult.Error("DLC Dat file does not exist.");
        }
        
        if(!File.Exists(CdnDat))
        {
            return ValidationResult.Error("CDN Dat file does not exist.");
        }
        
        if(!File.Exists(NspDat))
        {
            return ValidationResult.Error("NSP Dat file does not exist.");
        }
        
        if(!Directory.Exists(ScanDir))
        {
            return ValidationResult.Error("Scan directory does not exist.");
        }
        
        if(ByLetter?.Length > 1)
        {
            return ValidationResult.Error("Letter filter must be a single letter.");
        }

        return base.Validate();
    }
}