// Independent copy of RayBan/laptop/src/vision.js; no runtime Ray-Ban dependency.
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import ort from 'onnxruntime-node';
import sharp from 'sharp';
import { postprocessYolo } from './postprocess.js';

const INPUT_SIZE = 640;
export const defaultModelPath = fileURLToPath(
  new URL('../../../Assets/SafeCapture/Models/yolov8n.onnx', import.meta.url),
);

export class YoloDetector {
  static async create(modelPath = process.env.SAFE_CAPTURE_MODEL_PATH || defaultModelPath) {
    const session = await ort.InferenceSession.create(path.resolve(modelPath), {
      executionProviders: ['cpu'],
    });
    return new YoloDetector(session);
  }

  constructor(session) {
    this.session = session;
    this.inputName = session.inputNames[0];
    this.outputName = session.outputNames[0];
  }

  async detect(jpeg, width, height) {
    const { data } = await sharp(jpeg)
      .resize(INPUT_SIZE, INPUT_SIZE, { fit: 'fill' })
      .toColourspace('srgb')
      .removeAlpha()
      .raw()
      .toBuffer({ resolveWithObject: true });
    const planeSize = INPUT_SIZE * INPUT_SIZE;
    const tensorData = new Float32Array(3 * planeSize);
    for (let pixel = 0; pixel < planeSize; pixel += 1) {
      tensorData[pixel] = data[pixel * 3] / 255;
      tensorData[planeSize + pixel] = data[pixel * 3 + 1] / 255;
      tensorData[2 * planeSize + pixel] = data[pixel * 3 + 2] / 255;
    }
    const input = new ort.Tensor('float32', tensorData, [1, 3, INPUT_SIZE, INPUT_SIZE]);
    const outputs = await this.session.run({ [this.inputName]: input });
    const output = outputs[this.outputName];
    return postprocessYolo(output.data, output.dims, width, height);
  }
}
