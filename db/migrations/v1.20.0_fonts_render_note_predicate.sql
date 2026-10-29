-- fonts.render_note の列の説明を、主語を除いた述部を持つ形に合わせる（主語「組見本は」「書体見本と組見本は」はサイトが付ける）。
-- 列の型・既定値は変えない。何度流しても同じ結果になる。
ALTER TABLE `fonts`
  MODIFY COLUMN `render_note` varchar(128) DEFAULT NULL COMMENT '見本に添える注記の述部（主語「組見本は」「書体見本と組見本は」はサイトが付ける。NULL なら、書体名かウェイトが違うときに「代用」の文を自動で出す）';
