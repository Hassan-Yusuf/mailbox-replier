import type { DraftVariant } from '../types';

type Props = {
  variants: DraftVariant[];
  activeIndex: number;
  editedBodies: Record<number, string>;
  onSelect: (index: number) => void;
  onBodyChange: (variantId: number, body: string) => void;
};

export function VariantTabs({ variants, activeIndex, editedBodies, onSelect, onBodyChange }: Props) {
  const active = variants[activeIndex];

  return (
    <div className="variant-tabs">
      <div className="variant-tab-bar">
        {variants.map((variant, i) => (
          <button
            key={variant.id}
            className={`variant-tab-btn${i === activeIndex ? ' active' : ''}`}
            onClick={() => onSelect(i)}
            title={`${Math.round(variant.confidenceScore * 100)}% confidence (0.5 = baseline, 0.9+ = high)`}
          >
            {i + 1}. {variant.shapeLabel}
            {editedBodies[variant.id] !== undefined && editedBodies[variant.id] !== variant.body && (
              <span className="variant-edited-dot" title="Edited" />
            )}
          </button>
        ))}
      </div>
      {active && (
        <>
          <textarea
            className="variant-body variant-body-editable"
            value={editedBodies[active.id] ?? active.body}
            onChange={event => onBodyChange(active.id, event.target.value)}
            spellCheck
          />
          {active.groundingWarning && (
            <div className="grounding-warning">
              Grounding warning: {active.groundingWarning}
            </div>
          )}
        </>
      )}
    </div>
  );
}
