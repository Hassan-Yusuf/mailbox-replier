import type { DraftVariant } from '../types';

type Props = {
  variants: DraftVariant[];
  activeIndex: number;
  onSelect: (index: number) => void;
};

export function VariantTabs({ variants, activeIndex, onSelect }: Props) {
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
          </button>
        ))}
      </div>
      {active && (
        <>
          <div className="variant-body">{active.body}</div>
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
