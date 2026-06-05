// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using osu.Framework.Graphics.Textures;
using osu.Framework.Logging;
using osu.Framework.Text;

namespace osu.Framework.IO.Stores
{
    /// <summary>
    /// A glyph store that rasterizes glyphs from outlines.
    /// </summary>
    public class OutlineGlyphStore : IGlyphStore, IResourceStore<TextureUpload>
    {
        protected OutlineFont Font { get; }

        private RawFontVariation? rawVariation;

        public FontVariation? Variation { get; }

        public string FontName { get; }

        public float? Baseline => Font.Baseline;

        private readonly bool selfContained;

        /// <summary>
        /// Create a glyph store for a font using the specified OpenType named instance.
        /// </summary>
        /// <param name="font">The underlying font.</param>
        /// <param name="namedInstance">The named instance to select.</param>
        /// <param name="nameOverride">
        /// The value of <see cref="FontName"/>. If null, <paramref name="namedInstance"/> will be used.
        /// </param>
        public OutlineGlyphStore(OutlineFont font, string namedInstance, string? nameOverride = null)
            : this(font, new FontVariation { NamedInstance = namedInstance }, nameOverride)
        {
        }

        /// <summary>
        /// Create a glyph store for a font using the specified OpenType variation parameters.
        /// </summary>
        /// <param name="font">The underlying font.</param>
        /// <param name="variation">The font variation parameters.</param>
        /// <param name="nameOverride">
        /// The value of <see cref="FontName"/>. If null, it will be computed using a naming scheme based on
        /// <see href="https://download.macromedia.com/pub/developer/opentype/tech-notes/5902.AdobePSNameGeneration.html"/>.
        /// </param>
        public OutlineGlyphStore(OutlineFont font, FontVariation? variation = null, string? nameOverride = null)
        {
            Font = font;
            Variation = variation;

            FontName = nameOverride ?? variation?.GenerateInstanceName(font.AssetName) ?? font.AssetName;
        }

        /// <summary>
        /// Load a new font and create a glyph store for it.
        /// </summary>
        /// <param name="store">The font's resource store.</param>
        /// <param name="assetName">The asset name of the font.</param>
        public OutlineGlyphStore(IResourceStore<byte[]> store, string assetName)
            : this(new OutlineFont(store, assetName, 0) { Resolution = 100 }, (FontVariation?)null, assetName)
        {
            selfContained = true;
        }

        ~OutlineGlyphStore()
        {
            Dispose(false);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool isDisposing)
        {
            if (selfContained)
                Font.Dispose();
        }

        public async Task LoadFontAsync()
        {
            try
            {
                await Font.LoadAsync().ConfigureAwait(false);
                rawVariation = Font.DecodeFontVariation(Variation);
            }
            catch (Exception e)
            {
                Logger.Error(e, $"Couldn't load font {FontName} from {Font.AssetPath}.");
                throw;
            }
        }

        public bool HasGlyph(Grapheme c)
        {
            return c.IsSingleScalarValue && Font.HasGlyph(((Rune)c).Value);
        }

        public CharacterGlyph? Get(Grapheme c)
        {
            var metrics = Font.GetMetrics(Font.GetGlyphIndex(c.CharValue), rawVariation);

            if (metrics is null)
                return null;

            return new CharacterGlyph(c, metrics.XOffset, metrics.YOffset, metrics.XAdvance, metrics.Baseline, this);
        }

        /// <summary>
        /// This is a convenience method that converts the character to a <see cref="Grapheme"/> and calls <see cref="Get(Grapheme)"/>.
        /// </summary>
        /// <param name="character">The character to retrieve.</param>
        public CharacterGlyph Get(char character)
        {
            return Get(new Grapheme(character));
        }

        public int GetKerning(Grapheme left, Grapheme right)
        {
            return Font.GetKerning(Font.GetGlyphIndex(left.CharValue), Font.GetGlyphIndex(right.CharValue), rawVariation);
        }

        Task<CharacterGlyph> IResourceStore<CharacterGlyph>.GetAsync(string name, CancellationToken cancellationToken)
            => Task.Run(() => ((IGlyphStore)this).Get(new Grapheme(name)), cancellationToken)!;

        CharacterGlyph IResourceStore<CharacterGlyph>.Get(string name) => Get(new Grapheme(name))!;

        public TextureUpload Get(string name)
        {
            Grapheme grapheme;

            // name is expected to be in the format "{Grapheme}" or "Font:{FontName}/{Grapheme}"
            // this is a shorthand to check if there is a font name in the lookup
            if (name.Length > 1)
            {
                // if FontName does not match, return null.
                if (!name.StartsWith($@"{FontName}/", StringComparison.Ordinal))
                    return null;

                grapheme = new Grapheme(name.AsSpan(FontName.Length + 1));
            }
            else
            {
                grapheme = new Grapheme(name);
            }

            char c = grapheme.CharValue;
            uint glyphIndex = Font.GetGlyphIndex(c);

            return Font.RasterizeGlyph(glyphIndex, rawVariation)!;
        }

        public async Task<TextureUpload> GetAsync(string name, CancellationToken cancellationToken = default)
        {
            if (name.Length > 1 && !name.StartsWith($@"{FontName}/", StringComparison.Ordinal))
                return null!;

            return Get(name);
        }

        public Stream GetStream(string name) => throw new NotSupportedException();

        public IEnumerable<string> GetAvailableResources()
        {
            return Font.GetAvailableChars().Select(c => $@"{FontName}/{c}");
        }
    }
}
